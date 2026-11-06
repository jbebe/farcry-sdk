using JackAll.Cli.Infrastructure;
using JackAll.Core.Vfs;
using JackAll.Tools.ShaderLabels;

namespace JackAll.Cli.Commands.Shader;

/// <summary>
/// Labels every Direct3D 9 shader object by the permutations that load it and the quality presets
/// that draw it, and writes the list the App reads (<c>assets/fc2.shaderlabels.tsv</c>). Hand labels
/// win over found ones: those already in the output are kept, and <c>--hand</c> adds more.
/// </summary>
public sealed class ShaderLabelCommand : CliCommand<LabelCommandSettings>
{
    protected override int Run(LabelCommandSettings settings, CancellationToken cancellationToken)
    {
        using GameVfs vfs = settings.OpenVfs([]);
        return LabelList.Write(settings, "fc2.shaderlabels.tsv", vfs, ShaderLabeler.FromGame(vfs),
            ShaderLabeler.Covers, "D3D9 shader objects");
    }
}
