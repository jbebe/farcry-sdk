using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Move;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>
/// Checks that a MOVE graph, or the XML it is built from, reads back to the bytes it came from.
/// </summary>
/// <remarks>
/// The writer renumbers every back-reference from object identity rather than replaying the
/// indices it read, so a byte-identical result is evidence about the pointer graph and not just
/// about the field layout. For XML input it reports what the document builds instead, since there
/// is no original to compare against.
/// </remarks>
public sealed class MoveVerifyCommand : CliCommand<MoveVerifyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The MOVE graph to check, or the .xml it is built from.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] input = CliIO.ReadInput(settings.Input);

        // The magic discriminates rather than the extension, so a graph saved under another name
        // still checks as one and an XML document is never fed to the binary reader.
        bool binary = input.Length >= 4 && "MVM\0"u8.SequenceEqual(input.AsSpan(0, 4));

        MoveFile file = binary
            ? MoveCodec.Load(input)
            : MoveXml.FromXml(CliIO.ReadInputText(settings.Input));
        byte[] rebuilt = MoveCodec.Save(file);

        string name = settings.Input.EscapeMarkup();
        AnsiConsole.MarkupLine(
            $"[grey]{name}[/]: {file.Objects.Count} objects, "
            + $"{file.StateMachine?.Field("nbState") ?? 0} states, flags 0x{file.Flags:X5}"
            + (file.IsNamed ? " [yellow](named: the engine refuses this)[/]" : string.Empty));

        if (!binary)
        {
            AnsiConsole.MarkupLine($"  builds {rebuilt.Length} bytes");
            return 0;
        }

        int common = input.AsSpan().CommonPrefixLength(rebuilt);
        if (common == input.Length && common == rebuilt.Length)
        {
            AnsiConsole.MarkupLine("  [green]round trip is byte-identical[/]");
            return 0;
        }

        AnsiConsole.MarkupLine(
            common == Math.Min(input.Length, rebuilt.Length)
                ? $"  [red]![/] rebuilt {rebuilt.Length} bytes from {input.Length}"
                : $"  [red]![/] differs at 0x{common:x}");
        return 1;
    }
}
