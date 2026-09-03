using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core;

/// <summary>
/// Finds and loads the support assets that ship beside an executable, so every front end resolves
/// names and `.fcb` members the same way.
/// </summary>
/// <remarks>
/// Both CLIs copy <c>assets/fc2.hashlist</c> and <c>assets/binary_classes.xml</c> next to the exe as
/// <c>.itemhashes</c>/<c>.fcbclasses</c>; the walk-up fallback keeps a <c>dotnet run</c> straight from
/// source working too. Neither is fatal if missing - names fall back to bare hashes and `.fcb` members
/// to BinHex, exactly as in the App.
/// </remarks>
public static class BundledAssets
{
    public static NameDatabase LoadNames()
    {
        string? path = Find(".itemhashes") ?? Find(Path.Combine("assets", "fc2.hashlist"));
        return path is null ? NameDatabase.LoadFrom([]) : NameDatabase.Load(path);
    }

    /// <summary>
    /// The names behind a MOVE graph's hashes, so a fragment files under
    /// <c>Pawn_Generic_Aim.1746764574.xml</c> rather than a bare number.
    /// </summary>
    /// <remarks>
    /// Decoration only: the number is what binds, so a missing table costs readability and nothing
    /// else. See <see cref="JackAll.Core.Format.Move.MoveNames"/> for how the rows are proved.
    /// </remarks>
    public static Format.Move.MoveNames LoadMoveNames()
    {
        string? path = Find(".movenames") ?? Find(Path.Combine("assets", "fc2.movenames.tsv"));
        return path is null ? Format.Move.MoveNames.Empty : Format.Move.MoveNames.Load(path);
    }

    public static FcbClassDefinitions LoadFcbClasses()
    {
        string? path = Find(".fcbclasses") ?? Find(Path.Combine("assets", "binary_classes.xml"));
        return path is null ? FcbClassDefinitions.Empty : FcbClassDefinitions.Load(path);
    }

    /// <summary>Resolves a bundled asset by its beside-the-exe name or its in-repo path, the same
    /// walk-up both loaders above use.</summary>
    public static string? FindAsset(string linkName, string repoRelativePath)
        => Find(linkName) ?? Find(repoRelativePath);

    private static string? Find(string relativePath)
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
