using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Takes a convex shape out of its list or extended mesh group, refreshing the caches that leaves stale.</summary>
public sealed class HkxRemoveCommand : CliCommand<HkxShapeSettings>
{
    protected override int Run(HkxShapeSettings settings, CancellationToken cancellationToken)
        => HkxCli.Edit(settings.Input, settings.Out, file => HkxShapeEdit.Remove(HkxCli.Shape(file, settings.Shape).Link));
}
