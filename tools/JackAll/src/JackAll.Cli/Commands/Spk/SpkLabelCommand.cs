using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Vfs;
using JackAll.Tools.SoundLabels;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Labels every retail `.spk` by where its sounds are used and writes <c>assets/fc2.soundlabels.tsv</c>,
/// which <c>tools/asset-tags/build.py</c> turns into the sound tags. Hand labels win over found ones:
/// those already in the output are kept, and <c>--hand</c> adds more.
/// </summary>
public sealed class SpkLabelCommand : CliCommand<LabelCommandSettings>
{
    protected override int Run(LabelCommandSettings settings, CancellationToken cancellationToken)
    {
        using GameVfs vfs = settings.OpenVfs([]);
        var found = SoundLabeler.FromGame(vfs, BundledAssets.LoadFcbClasses(), new SyncProgress(JsonOutput.Report));
        return LabelList.Write(settings, "fc2.soundlabels.tsv", vfs, found, SoundLabeler.Covers, ".spk files");
    }
}
