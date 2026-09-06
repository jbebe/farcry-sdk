using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Rml;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Xml.Linq;

namespace JackAll.Cli.Commands.Rml;

/// <summary>
/// Recompiles the XML <c>rml decode</c> produced back into an RML document. A whole document only —
/// to change one section of a world descriptor, stage that section as a mod fragment instead, which
/// leaves the rest of the file alone.
/// </summary>
public sealed class RmlEncodeCommand : CliCommand<RmlEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The decoded XML to recompile.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file>")]
        [Description("Output path (default: the input path with a .rml extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] rml = RmlDocument.Serialize(XElement.Parse(CliIO.ReadInputText(settings.Input)));

        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".rml");
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        CliIO.WriteOutput(outPath, rml);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
