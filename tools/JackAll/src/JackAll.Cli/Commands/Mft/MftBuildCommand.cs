using System.ComponentModel;
using System.Globalization;
using JackAll.Cli.Commands.World;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Tools.Mft;
using JackAll.Tools.Png;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mft;

/// <summary>
/// Draws a font from a TrueType or OpenType file into a mod layer, ready for a UI text to name as
/// its font family. See docs/docs/file-formats/mft.md.
/// </summary>
public sealed class MftBuildCommand : CliCommand<MftBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<font>")]
        [Description("A TrueType or OpenType file: .ttf, .otf or a .ttc collection.")]
        public string Input { get; init; } = null!;

        [CommandOption("-f|--face <face>")]
        [Description("Which font of the file, by its index or style name as 'mft faces' lists them (default: the first).")]
        public string? Face { get; init; }

        [CommandOption("-a|--axis <tag=value>")]
        [Description("A design axis of a variable font, such as wght=600; repeat for more.")]
        public string[] Axes { get; init; } = [];

        [CommandOption("-s|--size <px>")]
        [Description("The pixel size to draw at (default: 25, the game's own fonts' size).")]
        [DefaultValue(25)]
        public int Size { get; init; }

        [CommandOption("-n|--name <name>")]
        [Description("What the game knows the font by, and its family's name (default: <family>_<size>).")]
        public string? Name { get; init; }

        [CommandOption("-o|--out <layer>")]
        [Description("The mod layer to write into: the folder that holds its mods\\ folder.")]
        public string Out { get; init; } = null!;

        [CommandOption("-p|--preview <file.png>")]
        [Description("Also write sample text set in the font as the game sets it.")]
        public string? Preview { get; init; }

        [CommandOption("-g|--game <dir>")]
        [Description("A Far Cry 2 install whose fonts.mgb packages and UI dependency lists the font is added to, as fragments. Without it only the font's own files are written.")]
        public string? Game { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Out) ? ValidationResult.Error("--out is required.") : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] file = CliIO.ReadInput(settings.Input);
        IReadOnlyList<FontFace> faces = FontRaster.Faces(file);
        FontFace face = settings.Face is not { } wanted ? faces[0]
            : faces.FirstOrDefault(f => f.Index.ToString() == wanted || f.Style.Equals(wanted, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException(
                  $"The file holds no font '{wanted}'. It holds: {string.Join(", ", faces.Select(f => $"{f.Index} {f.Style}"))}.");
        string name = settings.Name ?? face.DefaultName(settings.Size);

        Dictionary<string, float> axes = settings.Axes.Select(a => a.Split('=', 2)).ToDictionary(
            p => p[0].Trim(),
            p => p.Length == 2 && float.TryParse(p[1], CultureInfo.InvariantCulture, out float v) ? v
                : throw new InvalidOperationException($"'{string.Join('=', p)}' is not an axis value; write it as wght=600."));
        BuiltFont built = MftBuilder.Build(file, face.Index, settings.Size, name, Path.GetFileName(settings.Input), axes);
        // Read by hash alone, so no hashlist.
        using GameVfs? vfs = settings.Game is { } game
            ? GameVfs.OpenForOriginalsOnly(
                GameInstall.TryOpen(game, out string error) ?? throw new InvalidOperationException(error), NameDatabase.LoadFrom([]))
            : null;
        SortedDictionary<string, byte[]> written = FontLayer.Files(built, path => vfs?.ReadOriginal(NameHash.Compute(path)));
        LayerFolder.Write(settings.Out, written);

        if (settings.Preview is { } preview)
        {
            (byte[] rgba, int width, int height) = MftPreview.Render(built.Font, built.Pages, MftPreview.SampleText);
            CliIO.WriteOutput(preview, PngImage.Encode(rgba, width, height));
        }

        AnsiConsole.MarkupLine(
            $"[green]{name.EscapeMarkup()}[/]: {face.ToString().EscapeMarkup()}"
            + string.Concat(axes.Select(a => $" {a.Key}={a.Value}")) + $" at {settings.Size} px, "
            + $"{built.Font.CharacterCount} characters on {built.Pages.Count} page(s), {written.Count} file(s) written.");
        if (built.Missing.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]The font lacks {built.Missing.Count} of the game's characters[/], which draw as nothing: "
                + string.Join(' ', built.Missing.Take(24).Select(c => $"U+{c:X4}")) + (built.Missing.Count > 24 ? " ..." : ""));
        }
        if (vfs is null)
        {
            AnsiConsole.MarkupLine("[yellow]No --game, so the font is in no fonts.mgb or dependency list yet; the game will not draw it.[/]");
        }
        return 0;
    }
}
