using System.ComponentModel;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Moves a convex shape in the model, refreshing the caches the move leaves stale.</summary>
public sealed class HkxMoveCommand : CliCommand<HkxMoveCommand.Settings>
{
    public sealed class Settings : HkxShapeSettings
    {
        [CommandOption("--by <x,y,z>")]
        [Description("How far to move it, in metres along the model's axes (Z up).")]
        public string? By { get; init; }

        [CommandOption("--to <x,y,z>")]
        [Description("Where to put it in the model: the point hkx tree prints after 'at'.")]
        public string? To { get; init; }

        public override ValidationResult Validate()
            => (By is null) == (To is null) ? ValidationResult.Error("Give one of --by and --to.") : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken) => HkxCli.Edit(settings.Input, settings.Out, file =>
    {
        (HkxLink link, Matrix4x4 toModel, Matrix4x4 parentToModel) = HkxCli.Shape(file, settings.Shape);
        Matrix4x4 moved = toModel;
        moved.Translation = settings.To is { } to ? HkxCli.Vector(to, "--to") : toModel.Translation + HkxCli.Vector(settings.By!, "--by");
        HkxShapeEdit.PlaceInModel(link, parentToModel, moved);
    });
}
