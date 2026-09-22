using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core;

/// <summary>
/// Finds and loads the support assets that ship beside an executable, so every front end resolves
/// names and `.fcb` members the same way.
/// </summary>
/// <remarks>
/// Both CLIs copy <c>assets/fc2.hashlist</c>, <c>assets/binary_classes.xml</c> and
/// <c>assets/component_schema.json</c> next to the exe as <c>.itemhashes</c>/<c>.fcbclasses</c>/
/// <c>.componentschema</c>, and the App copies them into its <c>data</c> folder; the walk-up fallback
/// keeps a <c>dotnet run</c> straight from source working too. None is fatal if missing - names fall
/// back to bare hashes and `.fcb` members to BinHex.
/// </remarks>
public static class BundledAssets
{
    public static NameDatabase LoadNames()
    {
        string? path = FindAsset(".itemhashes", Path.Combine("assets", "fc2.hashlist"));
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
        string? path = FindAsset(".movenames", Path.Combine("assets", "fc2.movenames.tsv"));
        return path is null ? Format.Move.MoveNames.Empty : Format.Move.MoveNames.Load(path);
    }

    public static FcbClassDefinitions LoadFcbClasses()
    {
        string? path = FindAsset(".fcbclasses", Path.Combine("assets", "binary_classes.xml"));
        return path is null ? FcbClassDefinitions.Empty : FcbClassDefinitions.Load(path, LoadComponentSchema());
    }

    public static ComponentSchema LoadComponentSchema()
    {
        string? path = FindAsset(".componentschema", Path.Combine("assets", "component_schema.json"));
        return path is null ? ComponentSchema.Empty : ComponentSchema.Load(path);
    }

    /// <summary>Resolves a bundled asset by its name beside the exe or in its <c>data</c> folder, then by
    /// its in-repo path.</summary>
    public static string? FindAsset(string linkName, string repoRelativePath)
        => Find(linkName) ?? Find(Path.Combine("data", linkName)) ?? Find(repoRelativePath);

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
