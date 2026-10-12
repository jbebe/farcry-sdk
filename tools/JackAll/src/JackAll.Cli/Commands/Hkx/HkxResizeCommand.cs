using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Resizes a collision box, about its centre or by moving one face, refreshing the caches the change leaves stale.</summary>
public sealed class HkxResizeCommand : CliCommand<HkxResizeCommand.Settings>
{
    private static readonly string[] FaceNames = ["+x", "-x", "+y", "-y", "+z", "-z"];

    public sealed class Settings : HkxShapeSettings
    {
        [CommandOption("--size <x,y,z>")]
        [Description("Its new size from surface to surface along its own axes, about its centre.")]
        public string? Size { get; init; }

        [CommandOption("--face <face>")]
        [Description("The face to move with --by: +x, -x, +y, -y, +z or -z, along the box's own axes. The opposite face stays put.")]
        public string? Face { get; init; }

        [CommandOption("--by <metres>")]
        [Description("How far to move --face outward; negative moves it in.")]
        public float? By { get; init; }

        public override ValidationResult Validate() => (Size, Face, By) switch
        {
            (not null, null, null) => ValidationResult.Success(),
            (null, { } face, not null) when FaceNames.Contains(face) => ValidationResult.Success(),
            (null, not null, not null) => ValidationResult.Error("--face takes +x, -x, +y, -y, +z or -z."),
            _ => ValidationResult.Error("Give --size, or --face with --by."),
        };
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken) => HkxCli.Edit(settings.Input, settings.Out, file =>
    {
        (HkxLink link, HkxPrimitive shape) = HkxCli.Shape(file, settings.Shape);
        HkxObject box = HkxShapeEdit.Leaf(link);
        if (box.Class.Name != "hkpBoxShape")
        {
            throw new ArgumentException($"{settings.Shape} is a {box.Class.Name}; only boxes resize.");
        }
        Vector3 size = HkxShapeEdit.Size(box);
        if (settings.Size is { } wanted)
        {
            HkxShapeEdit.Resize(box, HkxCli.Vector(wanted, "--size"));
        }
        else
        {
            int face = Array.IndexOf(FaceNames, settings.Face);
            HkxShapeEdit.Stretch(link, HkxShapeEdit.ParentToModel(link, shape.ToModel), shape.ToModel, size, face, size[face / 2] + settings.By!.Value);
        }
        Vector3 now = HkxShapeEdit.Size(box);
        AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture, $"  [grey]size[/] {now.X:0.###} x {now.Y:0.###} x {now.Z:0.###}"));
    });
}
