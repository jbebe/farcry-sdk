using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Vfs;
using JackAll.Tools.Fc2Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fc2Model;

/// <summary>
/// Collects a model and everything it references out of a game install into one decoded pack.
/// </summary>
/// <remarks>
/// This is the half of the flow that faces the editor: the pack it produces carries no Dunia format
/// at all, so whatever opens it needs no format code. Applying one back is <c>fc2model apply</c>.
/// </remarks>
public sealed class Fc2ModelExportCommand : CliCommand<Fc2ModelExportCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandArgument(0, "<model>")]
        [Description("The model's game-relative path, e.g. graphics/weapons/primary/ak47/ak47.xbg.")]
        public string Model { get; init; } = string.Empty;

        [CommandOption("-o|--out <file.fc2model>")]
        [Description("Where to write the pack (default: the model's name beside the working directory).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        using GameVfs vfs = GameVfs.Load(
            install, BundledAssets.LoadNames(), GameCache.Load(install.CacheFile),
            BundledAssets.LoadFcbClasses(), new SyncProgress(JsonOutput.Report), includeFragments: false);

        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(settings.Model, vfs.ReadByPath);
        string output = settings.Out
            ?? Path.GetFileNameWithoutExtension(settings.Model) + Fc2ModelBundle.Extension;
        bundle.Save(output);

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                model = bundle.Manifest.Model,
                entries = bundle.Manifest.Entries.Count,
                output,
            });
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Packed {bundle.Manifest.Entries.Count} files for {bundle.Manifest.Model}");
        CliIO.ReportWrote(output);
        return 0;
    }
}
