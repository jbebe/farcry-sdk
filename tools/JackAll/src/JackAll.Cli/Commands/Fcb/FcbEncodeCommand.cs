using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fcb;

/// <summary>
/// Re-encodes a (possibly hand-edited) XML index back into an .fcb — the reverse of <c>fcb decode</c>
/// and the CLI counterpart of the App's Fcb import. External sub-files referenced by the index's
/// <c>external="…"</c> attributes are resolved from the same folder as the index, exactly as the App
/// does. Needs no class config: the XML already carries each value's type and each name/hash. Validates
/// the result by round-tripping it back through <see cref="FcbDocument.Deserialize"/>.
/// </summary>
public sealed class FcbEncodeCommand : CliCommand<FcbEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<index.xml>")]
        [Description("The XML index file produced by `fcb decode` (external sub-files are read from its folder).")]
        public string Index { get; init; } = null!;

        [CommandOption("-o|--out <file.fcb>")]
        [Description("Output .fcb path (default: the index path with an .fcb extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Index, ".fcb");
        CliIO.GuardNotOverwritingInput(settings.Index, outPath);

        string indexXml = CliIO.ReadInputText(settings.Index);
        string folder = Path.GetDirectoryName(Path.GetFullPath(settings.Index)) ?? ".";

        FcbObject root = FcbXml.FromXml(indexXml, name => File.ReadAllText(Path.Combine(folder, name)));
        byte[] fcb = FcbDocument.Serialize(root);

        FcbDocument.Deserialize(fcb); // validity check, same as the App before staging

        CliIO.WriteOutput(outPath, fcb);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
