using System.ComponentModel;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;
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
        (HkxLink link, Matrix4x4 toModel, Matrix4x4 parentToModel) = HkxCli.Shape(file, settings.Shape);
        GizmoAxis axis = Enum.Parse<GizmoAxis>(settings.Axis, ignoreCase: true);
        HkxShapeEdit.PlaceInModel(link, parentToModel, RotateGizmo.About(toModel, toModel.Translation, axis, settings.Degrees!.Value));
    });
}
