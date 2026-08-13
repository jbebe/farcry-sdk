using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.ModInstaller;

/// <summary>
/// jackall-mi: the mod installer. See the csproj for why it's a separate exe from jackall-cli.
/// </summary>
/// <remarks>
/// Progress goes to stderr and the result document to stdout, so a caller can pipe stderr into a
/// progress bar and still <c>JSON.parse</c> stdout whole. Every command emits a document either way -
/// a failure is <c>{"ok":false,"error":"…"}</c> plus exit 1, never a bare message.
/// </remarks>
internal static class Program
{
    /// <summary>Internal so CommandLineTests parses with the shipped grammar, not its own copy.</summary>
    internal static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "g", "layer", "l", "from", "f", "out", "o", "name", "force", "json", "help", "h",
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.Error.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        CommandLine cli;
        try
        {
            cli = CommandLine.Parse(args, KnownFlags);
        }
        catch (Exception ex)
        {
            // Parse failures predate knowing whether --json was asked for, so report both ways.
            Console.Error.WriteLine(ex.Message);
            Fail(args.Contains("--json"), ex.Message);
            return 1;
        }

        try
        {
            return cli.Command.ToLowerInvariant() switch
            {
                "mod status" => Status(cli),
                "mod build" => Build(cli),
                "mod import-legacy" => ImportLegacy(cli),
                "mod restore" => Restore(cli),
                _ => Unknown(cli),
            };
        }
        catch (Exception ex)
        {
            Fail(cli.Has("json"), ex.Message);
            return 1;
        }
    }

    // ---------------------------------------------------------------- commands

    /// <summary>
    /// The command a mod manager runs first, and the only one that reports an unusable install as
    /// *data* rather than a failure: "that folder isn't Far Cry 2" is a normal answer to ask for, and
    /// the caller needs the reason string to show a user, not an exit code.
    /// </summary>
    private static int Status(CommandLine cli)
    {
        string game = GameOption(cli);
        GameInstall? install = GameInstall.TryOpen(game, out string error);

        if (install is null)
        {
            Emit(cli, new StatusPayload
            {
                GamePath = Path.GetFullPath(game),
                Valid = false,
                Error = error,
            }, ModInstallerJson.Default.StatusPayload, $"Not a Far Cry 2 install: {error}");
            return 0;
        }

        bool hasBackup = install.HasVanillaBackup;
        bool looksModded = install.LooksModded();
        int patchEntries = install.TryCountPatchEntries();

        Emit(cli, new StatusPayload
        {
            GamePath = install.RootPath,
            Valid = true,
            DataDir = install.DataDir,
            PatchFat = install.PatchFat,
            PatchDat = install.PatchDat,
            HasVanillaBackup = hasBackup,
            LooksModded = looksModded,
            PatchEntries = patchEntries,
            NeedsVanillaConfirmation = !hasBackup && looksModded,
        }, ModInstallerJson.Default.StatusPayload,
            $"Far Cry 2 at {install.RootPath}{Environment.NewLine}"
            + $"  patch.fat entries : {patchEntries:N0}{Environment.NewLine}"
            + $"  vanilla backup    : {(hasBackup ? "present" : "not created yet")}"
            + (!hasBackup && looksModded
                ? Environment.NewLine
                  + "  The current patch.dat already looks modded. Restore it (verify the game files) "
                  + "before building, or the next build treats someone else's mod as the base game."
                : string.Empty));
        return 0;
    }

    /// <summary>
    /// Compiles the vanilla patch plus the given layers into the game's patch.dat/patch.fat.
    /// <c>--layer</c> order is the whole interface: it maps one-for-one onto PatchBuilder's layer list,
    /// where later wins. Nothing here reorders or deduplicates it, because the caller - a mod manager
    /// with a load-order UI - is the only thing that knows what the user asked for.
    /// </summary>
    private static int Build(CommandLine cli)
    {
        GameInstall install = OpenInstall(cli);
        GuardVanillaBackup(install, cli.Has("force"));

        IReadOnlyList<string> layerPaths = [.. cli.Values("layer"), .. cli.Values("l")];
        List<IModLayer> layers = [.. layerPaths.Select(ModPipeline.OpenLayer)];
        BuildResult result = ModPipeline.Build(install, layers, new SyncProgress(Report));

        Emit(cli, new BuildPayload
        {
            PatchFat = install.PatchFat,
            PatchDat = install.PatchDat,
            TotalEntries = result.TotalEntries,
            VanillaEntries = result.VanillaEntries,
            OverriddenEntries = result.OverriddenEntries,
            AddedEntries = result.AddedEntries,
            OutputBytes = result.OutputBytes,
            Layers = [.. layers.Select((layer, index) => new BuildLayerPayload
            {
                Index = index,
                Path = layerPaths[index],
                Name = layer.Name,
                WholeFileOverrides = layer.Hashes.Count,
                FragmentOverrides = layer.FragmentOverrides.Sum(kv => kv.Value.Count),
            })],
            Conflicts = [.. result.Conflicts.Select(ToPayload)],
        }, ModInstallerJson.Default.BuildPayload,
            $"Built {install.PatchDat} - {result.TotalEntries:N0} entries "
            + $"({result.OverriddenEntries:N0} overridden, {result.AddedEntries:N0} added, "
            + $"{result.OutputBytes / 1024.0 / 1024.0:N1} MB)");

        foreach (FragmentConflict conflict in result.Conflicts)
        {
            Report($"Warning: '{conflict.WinningLayer}' overrode '{string.Join(", ", conflict.EarlierLayers)}' "
                + $"inside '{conflict.FragmentId}' by load order - their edits genuinely conflicted, so only "
                + "the higher-priority mod's change survived.");
        }
        return 0;
    }

    /// <summary>
    /// Converts a legacy full-patch mod - a whole replacement patch.dat/patch.fat - into an ordinary
    /// layer folder. Worth doing rather than copying the pair in, because a legacy patch is ~200,000
    /// entries of which a handful are the mod, and only what genuinely differs gets staged.
    /// </summary>
    private static int ImportLegacy(CommandLine cli)
    {
        GameInstall install = OpenInstall(cli);
        string from = cli.Required("from", "the legacy mod's zip or folder.");
        string outDir = cli.Required("out", "where to write the converted layer.");
        bool isZip = ModPipeline.IsZipSource(from);

        Directory.CreateDirectory(outDir);
        string name = cli.Value("name") ?? ModPipeline.DefaultLayerName(outDir);
        var workspace = new FolderModLayer(outDir, name);

        FcbClassDefinitions definitions = BundledAssets.LoadFcbClasses();
        NameDatabase names = BundledAssets.LoadNames();
        var progress = new SyncProgress(Report);

        Report("Mounting the game's archives to diff against…");
        using GameVfs vfs = ModPipeline.OpenOriginals(install, names, progress);

        LegacyImportResult result = isZip
            ? LegacyPatchImporter.Import(
                from, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress)
            : LegacyPatchImporter.ImportFromDirectory(
                from, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress);

        ModPipeline.SaveCache(vfs, install);

        Emit(cli, new ImportLegacyPayload
        {
            OutDir = Path.GetFullPath(outDir),
            Name = name,
            TotalEntries = result.TotalEntries,
            Imported = result.Imported,
            FragmentsImported = result.FragmentsImported,
            Skipped = result.Skipped,
            StagedFiles = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Count(),
        }, ModInstallerJson.Default.ImportLegacyPayload,
            $"Imported {result.Imported:N0} file(s) and {result.FragmentsImported:N0} .fcb fragment(s) "
            + $"into {outDir} (out of {result.TotalEntries:N0} entries; {result.Skipped:N0} matched the "
            + "base game and were skipped).");
        return 0;
    }

    /// <summary>Puts the pristine patch.dat/patch.fat back, undoing every build - a mod manager's purge.</summary>
    private static int Restore(CommandLine cli)
    {
        GameInstall install = OpenInstall(cli);

        // GameInstall.RestoreVanilla throws for this too, but its message is written for someone
        // looking at a UI that already told them a backup exists - here the caller may never have built
        // at all, and "nothing to undo" is the useful thing to say.
        if (!install.HasVanillaBackup)
        {
            throw new InvalidOperationException(
                "There is no patch.dat.vanilla backup to restore from - this install has never been built "
                + "by JackAll, so there is nothing to undo.");
        }

        install.RestoreVanilla();

        Emit(cli, new RestorePayload
        {
            PatchFat = install.PatchFat,
            PatchDat = install.PatchDat,
        }, ModInstallerJson.Default.RestorePayload,
            $"Restored the original patch.dat/patch.fat in {install.DataDir}");
        return 0;
    }

    private static int Unknown(CommandLine cli)
    {
        string message = string.IsNullOrWhiteSpace(cli.Command)
            ? "No command given."
            : $"Unknown command '{cli.Command}'.";
        Fail(cli.Has("json"), $"{message} Run with --help for the four supported commands.");
        return 1;
    }

    // ------------------------------------------------------------------ shared

    /// <summary>
    /// Refuses to snapshot a patch archive that already carries somebody's mod as this install's
    /// "vanilla" (see GameInstall.BackupWouldCaptureMods). A headless run has nobody to ask, so it
    /// refuses and lets the caller decide with --force.
    /// </summary>
    private static void GuardVanillaBackup(GameInstall install, bool force)
    {
        if (force || !install.BackupWouldCaptureMods)
        {
            return;
        }
        throw new InvalidOperationException(
            "The current patch.dat already looks modded and there is no patch.dat.vanilla backup yet, so "
            + "building would capture someone else's mod as this install's base game - which cannot be "
            + "undone short of reinstalling. Restore the original files (verify the game's files in your "
            + "launcher) and try again, or pass --force if the current patch really is the baseline.");
    }

    /// <summary>
    /// <c>--game</c> is explicit rather than discovered: the caller is a mod manager that already knows
    /// where the game is, and silently guessing the wrong install is the one mistake here that damages
    /// something.
    /// </summary>
    private static string GameOption(CommandLine cli)
        => cli.Value("game") ?? cli.Value("g")
        ?? throw new ArgumentException("--game is required: point it at the Far Cry 2 install folder.");

    private static GameInstall OpenInstall(CommandLine cli)
        => GameInstall.TryOpen(GameOption(cli), out string error)
        ?? throw new InvalidOperationException(error);

    private static void Emit<T>(CommandLine cli, T payload, JsonTypeInfo<T> typeInfo, string humanText)
    {
        if (cli.Has("json"))
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, typeInfo));
        }
        else
        {
            Console.Out.WriteLine(humanText);
        }
    }

    private static void Fail(bool json, string message)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(
                new ErrorPayload { Error = message }, ModInstallerJson.Default.ErrorPayload));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }
    }

    /// <summary>Human-facing progress, always on stderr so it never contaminates the JSON document.</summary>
    private static void Report(string message) => Console.Error.WriteLine(message);

    private static ConflictPayload ToPayload(FragmentConflict c) => new()
    {
        FragmentId = c.FragmentId,
        IsNewEntry = c.IsNewEntry,
        WinningLayer = c.WinningLayer,
        EarlierLayers = c.EarlierLayers,
    };

    private const string Usage = """
        jackall-mi - Far Cry 2 mod installer

        Compiles mods into Data_Win32\patch.dat. The headless half of JackAll's Mods tab, and the whole
        surface a mod manager needs. Every command takes --json: one object on stdout, progress on stderr.

        Commands
          mod status         --game <dir>
          mod build          --game <dir> [--layer <path>]... [--force]
          mod import-legacy  --game <dir> --from <zip|dir> --out <dir> [--name <name>]
          mod restore        --game <dir>

        Options
          -g, --game <dir>   The install folder: the one with bin\FarCry2.exe and Data_Win32\.
          -l, --layer <path> A mod folder or .zip to apply. Repeatable; order matters, later wins.
          -f, --from <path>  A legacy mod: a .zip, or a folder holding a patch.fat/patch.dat pair.
          -o, --out <dir>    Where to write a converted layer. Created if missing.
              --name <name>  A converted layer's display name (default: the output folder's name).
              --force        Build even though patch.dat looks modded with no vanilla backup yet.
              --json         Emit one JSON object on stdout instead of human-readable text.

        For the asset-format commands (xbt, xbg, sbao, spk, fcb, rml, archive), use jackall-cli.
        """;
}
