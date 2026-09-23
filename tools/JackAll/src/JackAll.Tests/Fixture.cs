namespace JackAll.Tests;

/// <summary>
/// Locates the retail files under <c>Fixtures\</c>, and reports where two byte arrays first disagree.
/// </summary>
/// <remarks>
/// The files are Ubisoft-owned and never committed, so every test has to no-op cleanly without them.
/// Each fixture family has one canary tagged <c>[Trait("Category", "RequiresFixture")]</c>, which CI
/// excludes but a local run fails loudly.
/// </remarks>
internal static class Fixture
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>The file at this path under <c>Fixtures\</c>, or null when this checkout lacks it.</summary>
    public static string? Locate(string relativePath)
    {
        string path = Path.Combine(Root, relativePath);
        return File.Exists(path) || Directory.Exists(path) ? path : null;
    }

    public static byte[]? Read(string relativePath)
        => Locate(relativePath) is { } path ? File.ReadAllBytes(path) : null;

    public static string? ReadText(string relativePath)
        => Locate(relativePath) is { } path ? File.ReadAllText(path) : null;

    public static bool Present(params string[] relativePaths)
        => relativePaths.All(p => Locate(p) is not null);

    /// <summary>
    /// Stands in for an archive lookup: resolves a game path by its file name inside one fixture folder.
    /// </summary>
    public static Func<string, byte[]?> ByFileName(string folder)
        => gamePath => Read(Path.Combine(folder, Path.GetFileName(gamePath.Replace('\\', '/')).ToLowerInvariant()));

    /// <summary>Fails when any of these fixtures is missing - the body of every family's canary.</summary>
    public static void AssertPresent(params string[] relativePaths)
    {
        string[] missing = [.. relativePaths.Where(p => Locate(p) is null)];
        Assert.True(
            missing.Length == 0,
            $"{Root} lacks {string.Join(", ", missing)}, so the tests over them silently no-opped. "
            + "Copy them out of the game export to run them.");
    }

    public static void AssertSameBytes(string name, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        => Assert.True(FirstDifference(expected, actual) < 0, DescribeDifference(name, expected, actual));

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
