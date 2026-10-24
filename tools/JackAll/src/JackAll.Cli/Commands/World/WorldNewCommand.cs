using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.World;

/// <summary>Writes a new world's files, made from tmpla, into a mod layer folder.</summary>
/// <remarks>See docs/jackall/world/new-worlds.md.</remarks>
public sealed class WorldNewCommand : CliCommand<WorldNewCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The world's name: lowercase letters, digits and underscores, starting with a letter.")]
        public string Name { get; init; } = null!;

        [CommandOption("-s|--side <SECTORS>")]
        [Description("Sectors a side, 64 m each: 1 or 8 (default 8).")]
        public int Side { get; init; } = NewWorld.TemplateSide;

        [CommandOption("-o|--out <folder>")]
        [Description(@"An empty folder to write the layer to; the files land under its mods\.")]
        public string Out { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        LayerFolder.RequireEmpty(settings.Out);
        using GameVfs vfs = GameVfs.OpenForOriginalsOnly(settings.OpenInstall(), BundledAssets.LoadNames());
        if (vfs.ReadOriginal(NameHash.Compute($@"worlds\{settings.Name}\generated\{settings.Name}.game.xml")) is not null)
        {
            throw new InvalidOperationException($"The game ships a world named {settings.Name}.");
        }

        // The template as shipped, not with whatever the installed mods deployed into it.
        SortedDictionary<string, byte[]> files = NewWorld.Create(
            settings.Name, settings.Side, path => vfs.ReadOriginal(NameHash.Compute(path)));
        LayerFolder.Write(settings.Out, files);
        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, world = settings.Name, settings.Side, files = files.Count });
            return 0;
        }
        AnsiConsole.MarkupLine($"{settings.Name.EscapeMarkup()}: {files.Count} files, {settings.Side} x {settings.Side} sectors.");
        CliIO.ReportWrote(settings.Out);
        return 0;
    }
}
