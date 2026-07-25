using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Cli.Infrastructure;

/// <summary>
/// Loads the same bundled support assets the App ships, so a CLI decode resolves names/types exactly
/// the way the App's preview does. The .csproj copies <c>assets/fc2.hashlist</c> and
/// <c>assets/binary_classes.xml</c> next to the exe as <c>.itemhashes</c> / <c>.fcbclasses</c> (see
/// JackAll.Cli.csproj), so both are found immediately beside the running executable; the walk-up
/// fallback keeps a <c>dotnet run</c> straight from source working too. Neither is fatal if missing —
/// names just fall back to hashes and .fcb members to BinHex, same as the App.
/// </summary>
internal static class CliAssets
{
    public static NameDatabase LoadNames()
    {
        string? path = FindAsset(".itemhashes") ?? FindAsset(Path.Combine("assets", "fc2.hashlist"));
        return path is null ? NameDatabase.LoadFrom([]) : NameDatabase.Load(path);
    }

    public static FcbClassDefinitions LoadFcbClasses()
    {
        string? path = FindAsset(".fcbclasses") ?? FindAsset(Path.Combine("assets", "binary_classes.xml"));
        return path is null ? FcbClassDefinitions.Empty : FcbClassDefinitions.Load(path);
    }

    /// <summary>Walks up from the running exe's own directory looking for a bundled asset — next to the
    /// exe first (where the build copies <c>.itemhashes</c>/<c>.fcbclasses</c>), then up through the
    /// repo so a source checkout still finds <c>assets/…</c>.</summary>
    private static string? FindAsset(string relativePath)
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
