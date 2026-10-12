using System.ComponentModel;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Turns a convex shape about a model axis through where it sits, refreshing the caches the turn leaves stale.</summary>
public sealed class HkxTurnCommand : CliCommand<HkxTurnCommand.Settings>
{
    public sealed class Settings : HkxShapeSettings
    {
        [CommandOption("--axis <x|y|z>")]
        [Description("The model axis to turn about (default: z, which is up).")]
        [DefaultValue("z")]
        public string Axis { get; init; } = "z";

        [CommandOption("--degrees <angle>")]
        [Description("How far to turn it: counterclockwise looking down the axis.")]
        public float? Degrees { get; init; }

        public override ValidationResult Validate()
            => Degrees is null ? ValidationResult.Error("--degrees is required.")
                : Axis is not ("x" or "y" or "z") ? ValidationResult.Error("--axis takes x, y or z.")
                : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken) => HkxCli.Edit(settings.Input, settings.Out, file =>
    {
        (HkxLink link, HkxPrimitive shape) = HkxCli.Shape(file, settings.Shape);
        Vector3 axis = settings.Axis switch { "x" => Vector3.UnitX, "y" => Vector3.UnitY, _ => Vector3.UnitZ };
        Vector3 at = shape.ToModel.Translation;
        Matrix4x4 turn = Matrix4x4.CreateTranslation(-at) * Matrix4x4.CreateFromAxisAngle(axis, settings.Degrees!.Value * MathF.PI / 180) * Matrix4x4.CreateTranslation(at);
        HkxShapeEdit.PlaceInModel(link, HkxShapeEdit.ParentToModel(link, shape.ToModel), shape.ToModel * turn);
    });
}
