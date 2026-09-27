using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Spk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Spk;

/// <summary>Checks that an .spk bank survives decode and encode byte for byte.</summary>
public sealed class SpkVerifyCommand : CliCommand<SpkVerifyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.spk>")]
        [Description("The .spk sound bank to round-trip.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] original = CliIO.ReadInput(settings.Input);
        var files = new Dictionary<string, byte[]>();
        string xml = SpkBankXml.ToXml(SpkBank.Parse(original), (file, bytes) => files[file] = bytes);
        byte[] rebuilt = SpkBankXml.FromXml(xml, file => files[file]).Write();

        int same = original.AsSpan().CommonPrefixLength(rebuilt);
        if (same == original.Length && same == rebuilt.Length)
        {
            AnsiConsole.MarkupLine($"[green]OK[/] - {settings.Input.EscapeMarkup()} rebuilds byte for byte");
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"[red]Differs[/] at byte 0x{same:X} ({original.Length:N0} bytes in, {rebuilt.Length:N0} out)");
        return 1;
    }
}
