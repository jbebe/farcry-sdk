using JackAll.Cli.Infrastructure;
using JackAll.Tools.Spk;
using Spectre.Console.Cli;
using Spectre.Console;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Lists every record in an .spk sound bank - id, type, and a plain-language summary (audio
/// format/codec/size for a `FlatCopy` record, what it links to for the two metadata types) - so you
/// know which record id to hand to <c>spk extract</c>/<c>spk import</c>. The CLI counterpart of the
/// App's own <c>SpkFileHandler</c> grid (see <see cref="SpkPackage"/>'s remarks), as plain lines
/// rather than a grouped grid.
/// </summary>
public sealed class SpkListCommand : CliCommand<SpkListCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.spk>")]
        [Description("The .spk sound bank to list.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        SpkPackage package = SpkPackage.Parse(CliIO.ReadInput(settings.Input));

        AnsiConsole.MarkupLine(
            $"[bold]{Path.GetFileName(settings.Input).EscapeMarkup()}[/] — {package.Records.Count} record(s)");

        foreach (SpkRecord r in package.Records)
        {
            string kind = SpkFormat.DescribeKind(r);
            string summary = SpkFormat.DescribeSummary(package, r);
            AnsiConsole.MarkupLine($"  0x{r.Id:x8}  [aqua]{kind.EscapeMarkup()}[/]  {summary.EscapeMarkup()}");
        }

        return 0;
    }
}
