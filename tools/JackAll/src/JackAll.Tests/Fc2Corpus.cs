namespace JackAll.Tests;

/// <summary>
/// Locates the extracted retail export the format gates run against, and reports where two byte
/// arrays first disagree.
/// </summary>
/// <remarks>
/// The export is Ubisoft-owned and never committed, so every gate has to no-op cleanly without it.
/// A class that depends on it pairs its theories with a canary test tagged
/// <c>[Trait("Category", "RequiresFixture")]</c>, which CI excludes but a local run fails loudly -
/// otherwise a checkout with no corpus reports a green run that asserted nothing.
/// </remarks>
internal static class Fc2Corpus
{
    private const string OverrideVariable = "JACKALL_FC2_CORPUS";

    /// <summary>The export root, whether or not it exists.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(TestSupport.RepositoryRoot, "tmp", "gamefiles");

    public static bool Present => Directory.Exists(Root);

    /// <summary>Every file with this extension, in a stable order.</summary>
    public static IEnumerable<string> Find(string extension)
        => Present
            ? Directory.EnumerateFiles(Root, "*" + extension, SearchOption.AllDirectories).Order()
            : [];

    public static string MissingMessage(string extension)
        => $"{Root} holds no *{extension}, so every gate over them silently no-opped. "
           + $"Point {OverrideVariable} at an extracted export, or accept that this checkout cannot run them.";

    /// <summary>The first index at which the two differ, or -1 when they match.</summary>
    public static int FirstDifference(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        int limit = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < limit; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }
        return expected.Length == actual.Length ? -1 : limit;
    }

    /// <summary>Where two byte arrays diverge, phrased for an assertion message.</summary>
    public static string DescribeDifference(string path, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        int at = FirstDifference(expected, actual);
        if (at < 0)
        {
            return $"{Path.GetFileName(path)}: identical";
        }

        string expectedByte = at < expected.Length ? $"0x{expected[at]:X2}" : "end of file";
        string actualByte = at < actual.Length ? $"0x{actual[at]:X2}" : "end of file";
        return $"{Path.GetFileName(path)}: first difference at offset 0x{at:X} "
               + $"(original {expectedByte}, rewritten {actualByte}); "
               + $"{expected.Length} bytes in, {actual.Length} out.";
    }
}
