using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Commands.Xref;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
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
    public sealed class Settings : XrefFileSettings
    {
        [CommandArgument(0, "<model>")]
        [Description("The model's game-relative path, e.g. graphics/weapons/primary/ak47/ak47.xbg.")]
        public string Model { get; init; } = string.Empty;

        [CommandOption("-o|--out <file.fc2model>")]
        [Description("Where to write the pack (default: the model's name beside the working directory).")]
        public string? Out { get; init; }

        [CommandOption("--clip <path>")]
        [Description("An animation bank to carry along, by game path. Repeatable.")]
        public string[] Clip { get; init; } = [];

        [CommandOption("--clips")]
        [Description("Carry every animation bank that names this model. Reads every bank in the install.")]
        public bool Clips { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        using GameVfs vfs = GameVfs.Load(
            install, BundledAssets.LoadNames(), GameCache.Load(install.CacheFile),
            BundledAssets.LoadFcbClasses(), new SyncProgress(JsonOutput.Report), includeFragments: false);

        ReferenceIndex index = ReferenceIndex.Load(settings.ResolveIndexPath());
        Func<string, int>? usage = ReferenceUsage.Counter(index, settings.Model);
        if (usage is null)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No reference index[/]: ownership falls back to the directory rule, which "
                + "marks a pooled material shared even when only this model uses it. Run "
                + "'jackall-cli xref build' to get counts.");
        }

        List<string> clips = Clips(vfs, settings);
        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(
            settings.Model, vfs.ReadByPath, usage, clips);
        string output = settings.Out
            ?? Path.GetFileNameWithoutExtension(settings.Model) + Fc2ModelBundle.Extension;
        bundle.Save(output);

        int carried = bundle.Manifest.Entries.Count(entry => entry.Kind == Fc2ModelKind.Clip);
        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                model = bundle.Manifest.Model,
                entries = bundle.Manifest.Entries.Count,
                clips = carried,
                output,
            });
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Packed {bundle.Manifest.Entries.Count} files for {bundle.Manifest.Model}");
        if (carried > 0)
        {
            AnsiConsole.MarkupLineInterpolated($"  including {carried} animation bank(s)");
        }
        CliIO.ReportWrote(output);
        return 0;
    }

    /// <summary>
    /// Which banks to carry.
    /// </summary>
    /// <remarks>
    /// Nothing in a mesh names its animation - a weapon's motion is filed under the character
    /// animations - so there is no closure to walk. Naming banks one by one works when the paths are
    /// known; the search is for when they are not, and it asks the banks rather than guessing at
    /// folder names - see <see cref="ClipSearch"/>.
    /// </remarks>
    private static List<string> Clips(GameVfs vfs, Settings settings)
    {
        var clips = new List<string>(settings.Clip);
        if (!settings.Clips)
        {
            return clips;
        }

        List<string> banks = [.. vfs.Files.Values
            .Select(file => file.Path)
            .Where(path => path.EndsWith(".mab", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)];
        clips.AddRange(ClipSearch.For(settings.Model, banks, vfs.ReadByPath));
        return clips;
    }
}
