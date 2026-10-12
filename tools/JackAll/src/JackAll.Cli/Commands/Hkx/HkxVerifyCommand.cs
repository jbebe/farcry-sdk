using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Checks that each .hkx survives parse and write byte for byte.</summary>
public sealed class HkxVerifyCommand : CliCommand<HkxVerifyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The Havok files to round-trip.")]
        public string[] Inputs { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        int failed = 0;
        foreach (string input in settings.Inputs)
        {
            byte[] original = CliIO.ReadInput(input);
            byte[] rebuilt = HkxFile.Parse(original).Write();
            int same = original.AsSpan().CommonPrefixLength(rebuilt);
            if (same != original.Length || same != rebuilt.Length)
            {
                Console.WriteLine($"{input}: differs at 0x{same:X} ({original.Length:N0} bytes in, {rebuilt.Length:N0} out)");
                failed++;
            }
        }
        Console.WriteLine($"{settings.Inputs.Length - failed} of {settings.Inputs.Length} rebuild byte for byte");
        return failed == 0 ? 0 : 1;
    }
}
