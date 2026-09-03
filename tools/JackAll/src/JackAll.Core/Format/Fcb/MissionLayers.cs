namespace JackAll.Core.Format.Fcb;

/// <summary>
/// The mission-layer vocabulary of a world sector: which layer a placed entity structurally lives
/// under, and which layer its own mission component claims.
/// </summary>
/// <remarks>
/// The two are not the same thing and the engine uses them for different jobs - the nesting decides
/// whether the entity spawns at all, the component only files a live entity into a layer. See
/// docs/docs/engine-internals/entity-instancing.md; the practical consequence is that changing the
/// component alone moves nothing.
/// </remarks>
public static class MissionLayers
{
    /// <summary>The layer every sector has and the engine keeps unconditionally enabled.</summary>
    public const string MainName = "main";

    /// <summary>
    /// What a mission component holds when it names no layer. <c>CEntity::GetLayerName</c> tests for
    /// it before reading the field and answers <see cref="MainName"/> instead, so it means "unset",
    /// not "a layer whose id happens to be this".
    /// </summary>
    /// <remarks>
    /// Most shipped components carry it: across 40 untouched <c>w1_b_2</c> containers, 46 entities
    /// declare this and none declares a real layer id. Reading it as an id makes every one of them
    /// look like it disagrees with the layer it sits under.
    /// </remarks>
    public const uint NoLayer = 0xFFFFFFFF;

    /// <summary>A layer's authored path, e.g. <c>main</c> or <c>missions\outposts\w1_b_2\oiihvvl</c>.</summary>
    public static string NameOf(FcbObject layer) => FcbEntityFields.ReadString(layer, WorldHashes.TextPathId);

    /// <summary>A layer's own id, as stored rather than recomputed - a shipped layer's is not always
    /// the CRC32 of its path.</summary>
    public static uint? PathIdOf(FcbObject layer) => FcbEntityFields.ReadU32(layer, WorldHashes.PathId);

    public static bool IsMain(string layerName)
        => layerName.Equals(MainName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The layer this entity's own mission component claims, or null when it names none -
    /// whether by carrying no component at all or by carrying <see cref="NoLayer"/>. The engine reads
    /// both as <see cref="MainName"/>.</summary>
    public static uint? DeclaredLayerOf(FcbObject entity)
        => FcbEntityFields.FindComponent(entity, WorldHashes.CMissionComponent) is { } component
            && FcbEntityFields.ReadU32(component, WorldHashes.HidMissionLayerPath) is { } declared
            && declared != NoLayer
            ? declared
            : null;
}
