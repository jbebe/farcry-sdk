using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fcb;

/// <summary>
/// Decodes an .fcb to Gibbed-compatible XML — one index document plus, for an entity-library-shaped
/// root, one external sub-file per group (exactly the multi-export the App's Fcb handler writes). The
/// index and any externals land together in an output folder; <c>fcb encode</c> reads that folder back.
/// Class/member hashes resolve to readable names via the bundled <c>binary_classes.xml</c> (disable
/// with --no-classes to keep everything hash-only/BinHex).
/// </summary>
public sealed class FcbDecodeCommand : CliCommand<FcbDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.fcb>")]
        [Description("The .fcb to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out-dir <dir>")]
        [Description("Output folder for the XML index + external sub-files (default: a folder named after the input).")]
        public string? OutDir { get; init; }

        [CommandOption("--no-classes")]
        [Description("Don't load binary_classes.xml — emit hash-only/BinHex for every type and member.")]
        public bool NoClasses { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] data = CliIO.ReadInput(settings.Input);
        FcbObject root = FcbDocument.Deserialize(data);

        FcbClassDefinitions defs = settings.NoClasses ? FcbClassDefinitions.Empty : CliAssets.LoadFcbClasses();
        FcbXmlExport export = FcbXml.ToXml(root, defs);

        string baseName = Path.GetFileNameWithoutExtension(settings.Input);
        string outDir = settings.OutDir ?? CliIO.ResolveOutput(null, settings.Input, baseName);
        CliIO.EnsureDirectory(outDir);

        string indexPath = Path.Combine(outDir, baseName + ".xml");
        CliIO.WriteOutput(indexPath, export.IndexXml);
        CliIO.ReportWrote(indexPath);

        foreach ((string name, string xml) in export.ExternalFiles)
        {
            string path = Path.Combine(outDir, name);
            CliIO.WriteOutput(path, xml);
        }

        if (export.ExternalFiles.Count > 0)
        {
            AnsiConsole.MarkupLine($"[green]Wrote[/] {export.ExternalFiles.Count:N0} external sub-file(s) into {outDir.EscapeMarkup()}");
        }
        return 0;
    }
}
