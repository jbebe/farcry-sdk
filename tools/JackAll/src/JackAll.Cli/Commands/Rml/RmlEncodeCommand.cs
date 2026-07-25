using System.ComponentModel;
using System.Xml.Linq;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Rml;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Rml;

/// <summary>Re-encodes a (possibly hand-edited) XML document back into a binary .rml — the reverse of
/// <c>rml decode</c> and the CLI counterpart of the App's Rml import. Validates the result by
/// round-tripping it through <see cref="RmlDocument.Deserialize"/>.</summary>
public sealed class RmlEncodeCommand : CliCommand<RmlEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The XML produced by `rml decode` (possibly edited).")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.rml>")]
        [Description("Output .rml path (default: the input path with an .rml extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".rml");
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        XElement root = XDocument.Parse(CliIO.ReadInputText(settings.Input)).Root
            ?? throw new InvalidDataException("Empty XML document.");
        byte[] rml = RmlDocument.Serialize(root);

        RmlDocument.Deserialize(rml); // validity check, same as the App before staging

        CliIO.WriteOutput(outPath, rml);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
