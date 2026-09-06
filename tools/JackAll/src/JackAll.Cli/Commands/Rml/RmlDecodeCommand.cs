using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Rml;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Xml.Linq;

namespace JackAll.Cli.Commands.Rml;

/// <summary>
/// Decodes a compiled RML document — a <c>&lt;world&gt;.game.xml</c> or an <c>oasisstrings.rml</c> —
/// to readable XML; <c>rml encode</c> reads it back.
/// </summary>
public sealed class RmlDecodeCommand : CliCommand<RmlDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The compiled RML document to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output XML path (default: the input path with a .decoded.xml extension).")]
        public string? Out { get; init; }

        [CommandOption("-e|--element <name>")]
        [Description("Write only this top-level section, e.g. Environment.")]
        public string? Element { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        XElement root = RmlDocument.Deserialize(CliIO.ReadInput(settings.Input));

        XElement selected = settings.Element is null
            ? root
            : root.Descendants().FirstOrDefault(e => e.Name.LocalName == settings.Element)
              ?? throw new InvalidDataException(
                  $"This document holds no <{settings.Element}> element.");

        string outPath = CliIO.ResolveOutput(
            settings.Out, settings.Input,
            Path.GetFileNameWithoutExtension(settings.Input) + ".decoded.xml");

        CliIO.WriteOutput(outPath, selected.ToString());
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
