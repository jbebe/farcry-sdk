using System.ComponentModel;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Checks that each .hkx survives parse and write byte for byte, and optionally that its MOPPs hold.</summary>
public sealed class HkxVerifyCommand : CliCommand<HkxVerifyCommand.Settings>
{
    private const int ProbesPerMopp = 100;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The Havok files to round-trip.")]
        public string[] Inputs { get; init; } = [];

        [CommandOption("--mopp")]
        [Description("Also check each MOPP: that its code reaches exactly its collection's children, and that code rebuilt from them keeps every child a box touches.")]
        public bool Mopp { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        int failed = 0;
        var mopps = new MoppTally();
        foreach (string input in settings.Inputs)
        {
            byte[] original = CliIO.ReadInput(input);
            HkxFile file = HkxFile.Parse(original);
            byte[] rebuilt = file.Write();
            int same = original.AsSpan().CommonPrefixLength(rebuilt);
            if (same != original.Length || same != rebuilt.Length)
            {
                Console.WriteLine($"{input}: differs at 0x{same:X} ({original.Length:N0} bytes in, {rebuilt.Length:N0} out)");
                failed++;
            }
            if (settings.Mopp)
            {
                CheckMopps(input, file, mopps);
            }
        }

        Console.WriteLine($"{settings.Inputs.Length - failed} of {settings.Inputs.Length} rebuild byte for byte");
        if (settings.Mopp)
        {
            Console.WriteLine($"{mopps.Checked - mopps.Broken} of {mopps.Checked} rebuilt MOPPs hold ({mopps.BuiltBytes:N0} bytes of code, against {mopps.ShippedBytes:N0} shipped)");
            Console.WriteLine($"{mopps.Differ} shipped MOPPs reach other keys than their children");
            foreach ((string reason, int count) in mopps.Skipped)
            {
                Console.WriteLine($"{count} skipped: {reason}");
            }
        }
        return failed + mopps.Broken == 0 ? 0 : 1;
    }

    private static void CheckMopps(string input, HkxFile file, MoppTally tally)
    {
        foreach (HkxObject mopp in file.Objects().Where(o => o.Class.Name == "hkpMoppBvTreeShape"))
        {
            List<HkxMoppItem> items;
            try
            {
                items = HkxMopp.Items(mopp);
            }
            catch (NotSupportedException ex)
            {
                tally.Skipped[ex.Message] = tally.Skipped.GetValueOrDefault(ex.Message) + 1;
                continue;
            }

            (Vector4 shippedInfo, byte[] shipped) = HkxMopp.Code(mopp);
            (Vector4 info, byte[] built) = HkxMopp.Build(items);
            tally.Checked++;
            tally.ShippedBytes += shipped.Length;
            tally.BuiltBytes += built.Length;

            HkxMoppCheck original = HkxMopp.Check(items, shippedInfo, shipped, probes: 0);
            if (!original.Holds)
            {
                Console.WriteLine($"{input}: shipped code over {items.Count} children never reaches {original.Unreached.Count} of them and reaches {original.Stray.Count} others");
                tally.Differ++;
            }
            HkxMoppCheck rebuilt = HkxMopp.Check(items, info, built, ProbesPerMopp);
            if (!rebuilt.Holds)
            {
                Console.WriteLine($"{input}: rebuilt code over {items.Count} children never reaches {rebuilt.Unreached.Count} of them, "
                    + $"reaches {rebuilt.Stray.Count} others, and loses {rebuilt.Lost.Count} to boxes touching them");
                tally.Broken++;
            }
        }
    }

    private sealed class MoppTally
    {
        public int Checked, Broken, Differ, ShippedBytes, BuiltBytes;
        public Dictionary<string, int> Skipped { get; } = [];
    }
}
