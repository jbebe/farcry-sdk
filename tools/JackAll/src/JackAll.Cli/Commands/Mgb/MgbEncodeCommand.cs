using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Format.Mgb;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mgb;

/// <summary>Builds a binary .mgb (Magma UI package) from the XML <c>mgb decode</c> produces.</summary>
public sealed class MgbEncodeCommand : CliCommand<MgbEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The XML document to build.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.mgb>")]
        [Description("Output .mgb path (default: the input path with an .mgb extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".mgb");

        string xml = CliIO.ReadInputText(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        byte[] mgb = MgbXml.Encode(xml);

        // Read it straight back. This format has no lengths and no sentinels, so a package that
        // writes without complaint can still be unloadable; parsing it again is cheap and catches
        // that before it reaches the game.
        MgbPackage.Read(mgb);

        CliIO.WriteOutput(outPath, mgb);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
