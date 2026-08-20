namespace JackAll.Tools.World;

/// <summary>Which impostor card stands in for a RealTree species, and how tall to draw it.</summary>
public readonly record struct StandIn(string Mesh, float Height);

/// <summary>
/// A visible stand-in for the <c>.rtx</c> RealTree species, whose own format is not decoded.
/// </summary>
/// <remarks>
/// RealTree is a simulation asset - a tree that sways, burns and sheds branches - and nothing in the
/// file has been read past its header, so there is no geometry to draw. What the game does ship is
/// six camera-facing impostor cards under <c>vegetation\jungle\realtrees\donotuse</c>, real meshes
/// with real vegetation textures, placed by the scatter in their own right. Borrowing them puts
/// something plant-shaped where each RealTree stands.
/// <para>
/// This is a stand-in and should read as one: the card is not the species, and the heights below are
/// taken from the words the artists put in the file names - <c>tree</c>, <c>cactus</c>,
/// <c>_large</c>, <c>_med</c>, <c>_small</c> - not measured from anything. Real species geometry
/// needs the <c>.rtx</c> payload decoded.
/// </para>
/// </remarks>
public static class VegetationStandIn
{
    private const string Folder = @"graphics\vegetation\jungle\realtrees\donotuse\";

    /// <summary>Every impostor the substitution can reach, so a caller can bake them up front.</summary>
    public static IReadOnlyList<string> Meshes { get; } =
    [
        Folder + "facingbush.xbg",
        Folder + "facing_bush_large.xbg",
        Folder + "facing_bush_palm.xbg",
        Folder + "facing_bush_savannah.xbg",
        Folder + "facing_bush_large_savannah.xbg",
    ];

    /// <summary>Null for anything that is not a RealTree - an ordinary mesh stands in for itself.</summary>
    public static StandIn? For(string resourcePath)
    {
        if (!resourcePath.EndsWith(".rtx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(resourcePath);
        bool jungle = resourcePath.Contains(@"\jungle\", StringComparison.OrdinalIgnoreCase);
        bool tall = Has(name, "tree") || Has(name, "canopy");

        string mesh =
            Has(name, "palm") ? Folder + "facing_bush_palm.xbg"
            : tall ? Folder + (jungle ? "facing_bush_large.xbg" : "facing_bush_large_savannah.xbg")
            : Folder + (jungle ? "facingbush.xbg" : "facing_bush_savannah.xbg");

        float height = tall ? 12f : Has(name, "cactus") || Has(name, "saguaro") ? 4f : 3f;
        return new StandIn(mesh, height * SizeWord(name));
    }

    /// <summary>The artists' own size suffix, where a name carries one.</summary>
    private static float SizeWord(string name)
        => Has(name, "large") ? 1.4f
            : Has(name, "small") ? 0.5f
            : Has(name, "med") ? 0.8f
            : 1f;

    private static bool Has(string name, string word)
        => name.Contains(word, StringComparison.OrdinalIgnoreCase);
}
