using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Converts a legacy full-patch mod — the historical distribution format, a whole replacement
/// patch.dat/patch.fat meant to be dropped into Data_Win32 — into an ordinary layer folder.
/// </summary>
/// <remarks>
/// Worth doing rather than just copying the pair in, because a legacy patch is ~200,000 entries of
/// which a handful are the mod: <see cref="LegacyPatchImporter"/> diffs every one against the game's
/// own original and stages only what genuinely differs. What comes out is inspectable, orderable
/// against other mods, and mergeable at `.fcb` fragment level — none of which a dropped-in patch
/// archive can be.
///
/// Consequently this needs the game's archives mounted, so it is not cheap: expect it to take a
/// while on a first run. Progress goes to stderr throughout.
/// </remarks>
public sealed class ModImportLegacyCommand : CliCommand<ModImportLegacyCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandOption("-f|--from <path>")]
        [Description("The legacy mod: a .zip, or a folder containing a patch.fat/patch.dat pair.")]
        public string From { get; init; } = string.Empty;

        [CommandOption("-o|--out <dir>")]
        [Description("Where to write the converted layer. Created if it doesn't exist.")]
        public string Out { get; init; } = string.Empty;

        [CommandOption("--name <name>")]
        [Description("The layer's display name (default: the output folder's name).")]
        public string? Name { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            Spectre.Console.ValidationResult baseResult = base.Validate();
            if (!baseResult.Successful)
            {
                return baseResult;
            }
            if (string.IsNullOrWhiteSpace(From))
            {
                return Spectre.Console.ValidationResult.Error("--from is required: the legacy mod's zip or folder.");
            }
            return string.IsNullOrWhiteSpace(Out)
                ? Spectre.Console.ValidationResult.Error("--out is required: where to write the converted layer.")
                : Spectre.Console.ValidationResult.Success();
        }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        bool isZip = ModPipeline.IsZipSource(settings.From);

        Directory.CreateDirectory(settings.Out);
        string name = settings.Name ?? ModPipeline.DefaultLayerName(settings.Out);
        var workspace = new FolderModLayer(settings.Out, name);

        FcbClassDefinitions definitions = BundledAssets.LoadFcbClasses();
        NameDatabase names = BundledAssets.LoadNames();

        var progress = new SyncProgress(JsonOutput.Report);

        JsonOutput.Report("Mounting the game's archives to diff against…");
        using GameVfs vfs = ModPipeline.OpenOriginals(install, names, progress);

        LegacyImportResult result = isZip
            ? LegacyPatchImporter.Import(
                settings.From, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress)
            : LegacyPatchImporter.ImportFromDirectory(
                settings.From, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress);

        ModPipeline.SaveCache(vfs, install);

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                outDir = Path.GetFullPath(settings.Out),
                name,
                result.TotalEntries,
                result.Imported,
                result.FragmentsImported,
                result.Skipped,
                stagedFiles = Directory.EnumerateFiles(settings.Out, "*", SearchOption.AllDirectories).Count(),
            });
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[green]Imported[/] {result.Imported:N0} file(s) and {result.FragmentsImported:N0} .fcb fragment(s) "
            + $"into {settings.Out.EscapeMarkup()} (out of {result.TotalEntries:N0} entries; "
            + $"{result.Skipped:N0} matched the base game and were skipped).");
        return 0;
    }
}
