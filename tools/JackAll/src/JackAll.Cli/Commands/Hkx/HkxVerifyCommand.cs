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
        int failed = 0, broken = 0, differ = 0, mopps = 0, shippedBytes = 0, builtBytes = 0;
        Dictionary<string, int> skipped = [];
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
            if (!settings.Mopp)
            {
                continue;
            }

            foreach (HkxObject mopp in file.Objects().Where(o => o.Class.Name == "hkpMoppBvTreeShape"))
            {
                List<HkxMoppItem> items;
                try
                {
                    items = HkxMopp.Items(mopp.GetPointer("child.childShape")!);
                }
                catch (NotSupportedException ex)
                {
                    skipped[ex.Message] = skipped.GetValueOrDefault(ex.Message) + 1;
                    continue;
                }

                mopps++;
                byte[] shipped = HkxMopp.Code(mopp).Code;
                (Vector4 info, byte[] built) = HkxMopp.Build(items);
                (shippedBytes, builtBytes) = (shippedBytes + shipped.Length, builtBytes + built.Length);
                List<uint> keys = [.. items.Select(i => i.Key).Order()];
                List<uint> reached = [.. HkxMopp.Keys(shipped).Order()];
                if (!reached.SequenceEqual(keys))
                {
                    Console.WriteLine($"{input}: shipped code over {items.Count} children skips {keys.Except(reached).Count()} of them and reaches {reached.Except(keys).Count()} others");
                    differ++;
                }
                int lost = HkxMopp.Probes(items, ProbesPerMopp).Sum(p => HkxMopp.Lost(items, info, built, p).Count());
                string? problem = !HkxMopp.Keys(built).Order().SequenceEqual(keys) ? "reaches other keys than its children"
                    : lost > 0 ? $"lost {lost} children to boxes touching them"
                    : null;
                if (problem is not null)
                {
                    Console.WriteLine($"{input}: rebuilt code over {items.Count} children {problem}");
                    broken++;
                }
            }
        }

        Console.WriteLine($"{settings.Inputs.Length - failed} of {settings.Inputs.Length} rebuild byte for byte");
        if (settings.Mopp)
        {
            Console.WriteLine($"{mopps - broken} of {mopps} rebuilt MOPPs hold ({builtBytes:N0} bytes of code, against {shippedBytes:N0} shipped)");
            Console.WriteLine($"{differ} shipped MOPPs reach other keys than their children");
            foreach ((string reason, int count) in skipped)
            {
                Console.WriteLine($"{count} skipped: {reason}");
            }
        }
        return failed + broken == 0 ? 0 : 1;
    }
}
