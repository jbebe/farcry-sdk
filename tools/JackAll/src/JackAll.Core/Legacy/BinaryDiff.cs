using System.Reflection.PortableExecutable;

namespace JackAll.Core.Legacy;

/// <summary>
/// The byte runs a mod patched into a game binary, each addressed by RVA so it can be found in a
/// disassembler and turned into a pattern.
/// </summary>
public static class BinaryDiff
{
    /// <summary>Runs closer than this are one patch: a string or instruction edited in place
    /// rarely changes every byte it covers.</summary>
    private const int JoinGap = 8;

    /// <summary>How much of the base game's surrounding bytes each run carries for pattern work.</summary>
    private const int ContextBytes = 16;

    public static List<LegacyChange> Diff(string unit, byte[] vanilla, byte[] mod)
    {
        if (vanilla.AsSpan().SequenceEqual(mod))
        {
            return [];
        }

        if (Image.Read(vanilla) is not { } before || Image.Read(mod) is not { } after)
        {
            return [new LegacyChange(ChangeKind.File, unit, unit, $"{vanilla.Length} bytes", $"{mod.Length} bytes", Whole: true)];
        }

        // A re-stamped build with the same sections still diffs byte for byte; only a real
        // relayout makes every byte move.
        if (!before.Sections.Select(Layout).SequenceEqual(after.Sections.Select(Layout)))
        {
            return [new LegacyChange(ChangeKind.File, unit, unit,
                $"build {before.Stamp:X8}/{before.SizeOfImage:X}", $"build {after.Stamp:X8}/{after.SizeOfImage:X}",
                Whole: true, Hint: "a different build of this binary, not a patch of the base game's")];
        }

        List<LegacyChange> changes = [];
        foreach ((int start, int length) in Runs(vanilla, mod, 0, before.SizeOfHeaders))
        {
            changes.Add(Run(unit, $"@hdr+0x{start:x}", vanilla, mod, start, length, "PE headers"));
        }

        foreach (SectionHeader section in before.Sections)
        {
            int end = Math.Min(section.PointerToRawData + section.SizeOfRawData, Math.Min(vanilla.Length, mod.Length));
            foreach ((int start, int length) in Runs(vanilla, mod, section.PointerToRawData, end))
            {
                int rva = section.VirtualAddress + start - section.PointerToRawData;
                changes.Add(Run(unit, $"@0x{rva:x}", vanilla, mod, start, length, section.Name));
            }
        }

        int imageEnd = before.Sections.Max(s => s.PointerToRawData + s.SizeOfRawData);
        if (!vanilla.AsSpan(Math.Min(imageEnd, vanilla.Length)).SequenceEqual(mod.AsSpan(Math.Min(imageEnd, mod.Length))))
        {
            changes.Add(new LegacyChange(ChangeKind.Bytes, unit, $"{unit}@overlay",
                $"{vanilla.Length - imageEnd} bytes", $"{mod.Length - imageEnd} bytes",
                Hint: "data past the last section - an Authenticode signature, never mapped"));
        }

        return changes;
    }

    private static (string, int, int, int) Layout(SectionHeader section)
        => (section.Name, section.VirtualAddress, section.PointerToRawData, section.SizeOfRawData);

    private static LegacyChange Run(string unit, string at, byte[] vanilla, byte[] mod, int start, int length, string where)
    {
        int before = Math.Max(0, start - ContextBytes);
        int after = Math.Min(vanilla.Length, start + length + ContextBytes);
        return new LegacyChange(ChangeKind.Bytes, unit, unit + at,
            Convert.ToHexString(vanilla, start, length), Convert.ToHexString(mod, start, length),
            Hint: where,
            Context: $"{Convert.ToHexString(vanilla, before, start - before)}|{Convert.ToHexString(vanilla, start + length, after - start - length)}");
    }

    private static IEnumerable<(int Start, int Length)> Runs(byte[] a, byte[] b, int from, int to)
    {
        to = Math.Min(to, Math.Min(a.Length, b.Length));
        int? start = null;
        int lastDiff = -1;
        for (int i = from; i < to; i++)
        {
            if (a[i] == b[i])
            {
                continue;
            }

            if (start is { } open && i - lastDiff > JoinGap)
            {
                yield return (open, lastDiff - open + 1);
                start = null;
            }

            start ??= i;
            lastDiff = i;
        }

        if (start is { } last)
        {
            yield return (last, lastDiff - last + 1);
        }
    }

    private sealed record Image(uint Stamp, int SizeOfImage, int SizeOfHeaders, IReadOnlyList<SectionHeader> Sections)
    {
        public static Image? Read(byte[] bytes)
        {
            try
            {
                using var reader = new PEReader(new MemoryStream(bytes));
                PEHeaders headers = reader.PEHeaders;
                return headers.PEHeader is not { } pe
                    ? null
                    : new Image((uint)headers.CoffHeader.TimeDateStamp, pe.SizeOfImage, pe.SizeOfHeaders, headers.SectionHeaders);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }
    }
}
