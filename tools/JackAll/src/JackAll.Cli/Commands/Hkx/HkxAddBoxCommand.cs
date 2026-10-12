using System.ComponentModel;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Adds a collision box beside a shape, in its list or extended mesh group, refreshing the caches it leaves stale.</summary>
public sealed class HkxAddBoxCommand : CliCommand<HkxAddBoxCommand.Settings>
{
    public sealed class Settings : HkxShapeSettings
    {
        [CommandOption("--size <x,y,z>")]
        [Description("Its size from surface to surface along the model's axes.")]
        public string? Size { get; init; }

        [CommandOption("--at <x,y,z>")]
        [Description("Where its centre goes in the model.")]
        public string? At { get; init; }

        public override ValidationResult Validate()
            => Size is null || At is null ? ValidationResult.Error("--size and --at are both required.") : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken) => HkxCli.Edit(settings.Input, settings.Out, file =>
    {
        (HkxLink beside, _, Matrix4x4 parentToModel) = HkxCli.Shape(file, settings.Shape);
        HkxShapeEdit.AddBox(beside, parentToModel, HkxCli.Vector(settings.Size!, "--size"), Matrix4x4.CreateTranslation(HkxCli.Vector(settings.At!, "--at")));
    });
}
