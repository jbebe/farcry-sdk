using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Mft;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mft;

/// <summary>Lists the fonts a font file holds, by the index <see cref="MftBuildCommand"/> takes.</summary>
public sealed class MftFacesCommand : CliCommand<MftFacesCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<font>")]
        [Description("A TrueType or OpenType file: .ttf, .otf or a .ttc collection.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] file = CliIO.ReadInput(settings.Input);
        // A variable font's named styles share their face's axes, so each face lists them once.
        foreach (IGrouping<int, FontFace> styles in FontRaster.Faces(file).GroupBy(f => f.Index & 0xFFFF))
        {
            foreach (FontFace face in styles)
            {
                AnsiConsole.MarkupLine($"{face.Index,8}  {face.ToString().EscapeMarkup()}");
            }
            foreach (FontAxis axis in styles.First().Axes)
            {
                AnsiConsole.MarkupLine($"          axis {axis.Tag} ({axis.Name.EscapeMarkup()}): {axis.Minimum:0.##} to {axis.Maximum:0.##}, {axis.Default:0.##} by default");
            }
        }
        return 0;
    }
}
