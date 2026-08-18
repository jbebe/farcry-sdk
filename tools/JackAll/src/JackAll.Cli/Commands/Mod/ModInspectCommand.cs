using System.ComponentModel;
using System.IO.Compression;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Says what a folder or zip actually is — a layer, a legacy full-patch mod, or neither — and, for a
/// layer, where inside it the mod's tree really starts.
/// </summary>
/// <remarks>
/// A mod manager needs this before it stages anything: the two shapes are installed completely
/// differently (one is copied, the other has to go through <c>mod import-legacy</c> first), and a
/// community zip nearly always wraps its tree in a folder named after the mod, which has to be
/// stripped or every path hashes to nothing. Both answers come from
/// <see cref="ModLayerInspector"/>/<see cref="LegacyPatchImporter"/> rather than from any rule
/// invented here, so this can't disagree with what a later build will do.
///
/// Pass <c>--game</c> whenever possible: without it there's nothing to check a candidate root
/// against, so the tree is reported as-is and a wrapped mod will be misread.
/// </remarks>
public sealed class ModInspectCommand : CliCommand<ModInspectCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("A mod folder or .zip.")]
        public string Input { get; init; } = null!;

        [CommandOption("-g|--game <dir>")]
        [Description("The Far Cry 2 install, used to tell a correct root from a wrong one. Strongly recommended.")]
        public string? Game { get; init; }

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout instead of human-readable output; progress goes to stderr.")]
        public bool Json { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        bool isZip = ModPipeline.IsZipSource(settings.Input);

        Func<uint, bool>? entryExists = null;
        if (!string.IsNullOrWhiteSpace(settings.Game))
        {
            GameInstall install = GameInstall.TryOpen(settings.Game, out string error)
                ?? throw new InvalidOperationException(error);
            JsonOutput.Report("Reading the game's archive indexes to check candidate roots…");
            HashSet<uint> hashes = install.ReadBaseGameHashes(new SyncProgress(JsonOutput.Report));
            entryExists = hashes.Contains;
        }

        string[] relativePaths = isZip ? ZipPaths(settings.Input) : DirectoryPaths(settings.Input);

        if (FindLegacyPatch(settings.Input, isZip) is { } pair)
        {
            // Computed unconditionally now (previously this branch returned before relativePaths/
            // entryExists even existed) - a mod manager needs to know when an archive isn't *just* a
            // legacy patch pair, since only the pair gets converted; anything else in here would
            // otherwise be silently dropped with nobody told.
            ModLayerReport? sideContent = ScoreSideContent(settings.Input, isZip, relativePaths, pair, entryExists);
            return ReportLegacy(settings, pair, sideContent);
        }

        ModLayerReport report = ModLayerInspector.Inspect(relativePaths, entryExists);
        // A plugins-only tree is a deployable layer too: it builds a vanilla patch and still puts
        // its payload into bin\plugins.
        string kind = report.TotalOverrides > 0 || report.PluginFiles > 0 ? "layer" : "unknown";

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                path = Path.GetFullPath(settings.Input),
                kind,
                container = isZip ? "zip" : "directory",
                rootChecked = entryExists is not null,
                report.Root,
                report.WholeFileOverrides,
                report.FragmentOverrides,
                report.HashAddressed,
                report.UnknownEntries,
                report.IgnoredFiles,
                report.PluginFiles,
                report.SamplePaths,
                totalFiles = relativePaths.Length,
            });
            return 0;
        }

        if (kind == "unknown")
        {
            AnsiConsole.MarkupLine(
                "[yellow]Not a Far Cry 2 mod[/] - nothing in here classifies as layer content. "
                + "(A mod is a mods\\ folder of game paths - worlds\\…, _hash\\<crc32>.<ext> - "
                + "and/or a plugins\\ folder holding an FCSE plugin.)");
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Mod layer[/] ({relativePaths.Length:N0} file(s))");
        AnsiConsole.MarkupLine($"  root              : {(report.Root.Length == 0 ? "<top level>" : report.Root.EscapeMarkup())}");
        AnsiConsole.MarkupLine($"  file overrides    : {report.WholeFileOverrides:N0} ({report.HashAddressed:N0} hash-addressed)");
        AnsiConsole.MarkupLine($"  .fcb fragments    : {report.FragmentOverrides:N0}");
        AnsiConsole.MarkupLine($"  plugin files      : {report.PluginFiles:N0} (deployed to bin\\plugins)");
        if (entryExists is not null && report.UnknownEntries > 0)
        {
            AnsiConsole.MarkupLine($"  [yellow]not in the game   : {report.UnknownEntries:N0}[/] (files this mod adds, or a misread root)");
        }
        AnsiConsole.MarkupLine($"  ignored files     : {report.IgnoredFiles:N0} (readmes and the like)");
        return 0;
    }

    private int ReportLegacy(Settings settings, (string Fat, string Dat) pair, ModLayerReport? sideContent)
    {
        // The confirmed count, not TotalOverrides - see ScoreSideContent's remarks on why an
        // unconfirmed path (a readme, a screenshot) shouldn't be counted as real side content, only as
        // what gates whether sideContent is non-null in the first place.
        int confirmedSideFiles = sideContent?.RecognizedFiles ?? 0;

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                path = Path.GetFullPath(settings.Input),
                kind = "legacy-patch",
                container = Directory.Exists(settings.Input) ? "directory" : "zip",
                fat = pair.Fat,
                dat = pair.Dat,
                alsoContainsLayerFiles = sideContent is not null,
                layerFileOverrides = confirmedSideFiles,
                layerSamplePaths = sideContent?.SamplePaths ?? [],
            });
            return 0;
        }

        AnsiConsole.MarkupLine("[green]Legacy full-patch mod[/] - a whole replacement patch.dat/patch.fat pair.");
        AnsiConsole.MarkupLine($"  {pair.Fat.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  {pair.Dat.EscapeMarkup()}");
        if (sideContent is not null)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Also found {confirmedSideFiles:N0} file(s) elsewhere in this archive[/] that "
                + "separately resolve to real game files - only the patch.dat/patch.fat pair gets "
                + "converted by import-legacy, so these would be silently left out unless staged separately.");
        }
        AnsiConsole.MarkupLine("Run [blue]mod import-legacy[/] to convert it into an ordinary layer.");
        return 0;
    }

    private static (string Fat, string Dat)? FindLegacyPatch(string input, bool isZip)
        => isZip ? LegacyPatchImporter.FindPatchPairInZip(input) : LegacyPatchImporter.FindPatchPair(input);

    /// <summary>
    /// Whether real layer content exists *outside* the legacy patch pair itself - e.g. an author who
    /// zipped their content mod together with a full patch.dat/patch.fat backup, or a bonus FCSE
    /// plugin bundled alongside. Only <c>mods\</c>/<c>plugins\</c>-packaged files can qualify, so a
    /// readme or screenshot next to the pair never flags.
    /// </summary>
    private static ModLayerReport? ScoreSideContent(
        string input, bool isZip, string[] allRelativePaths, (string Fat, string Dat) pair,
        Func<uint, bool>? entryExists)
    {
        // FindPatchPair(InZip) returns the pair as whatever it naturally addresses them by - absolute
        // filesystem paths for a directory input, zip entry names for a zip - so exclusion has to
        // match relativePaths' own shape rather than assuming one.
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (isZip)
        {
            excluded.Add(pair.Fat);
            excluded.Add(pair.Dat);
        }
        else
        {
            excluded.Add(Path.GetRelativePath(input, pair.Fat));
            excluded.Add(Path.GetRelativePath(input, pair.Dat));
        }

        string[] rest = [.. allRelativePaths.Where(p => !excluded.Contains(p))];
        ModLayerReport report = ModLayerInspector.Inspect(rest, entryExists);

        // At least one *confirmed* entry (TotalOverrides beyond what UnknownEntries accounts for):
        // mods\-packaged files that hash to nothing the game has are junk packaging, not companion
        // content. A plugins\ payload is structural, so it needs no such confirmation.
        return report.TotalOverrides > report.UnknownEntries || report.PluginFiles > 0 ? report : null;
    }

    private static string[] ZipPaths(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return [.. zip.Entries
            .Where(e => !e.FullName.EndsWith('/') && e.Name.Length > 0)
            .Select(e => e.FullName)];
    }

    private static string[] DirectoryPaths(string root)
        => [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))];
}
