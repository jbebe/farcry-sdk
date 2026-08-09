namespace JackAll.Core.Tests;

/// <summary>
/// Locates the Domino script corpus the fixture-backed tests run against.
///
/// The default is the curated sample under <c>Fixtures\Domino\</c>, which is gitignored (the scripts
/// are Ubisoft's) - so every caller has to cope with it being absent, and the tests that use it
/// silently no-op rather than fail on a fresh clone.
///
/// Setting <c>JACKALL_DOMINO_CORPUS</c> to a directory containing <c>system\</c> and <c>user\</c>
/// points them at a full extraction instead. Worth doing when changing reconstruction: the sample has
/// 7 release/debug-twin pairs, a full extract has hundreds, and the twin cross-check is only as strong
/// as the number of graphs it can compare.
/// </summary>
internal static class DominoCorpus
{
    private const string OverrideVariable = "JACKALL_DOMINO_CORPUS";

    private static string Root =>
        Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine("Fixtures", "Domino");

    /// <summary>The authored mission graphs, or null when no corpus is available.</summary>
    public static string? UserDirectory => DirectoryOrNull("user");

    /// <summary>The reusable node-type library, or null when no corpus is available.</summary>
    public static string? SystemDirectory => DirectoryOrNull("system");

    private static string? DirectoryOrNull(string name)
    {
        string path = Path.Combine(Root, name);
        return Directory.Exists(path) ? path : null;
    }
}
