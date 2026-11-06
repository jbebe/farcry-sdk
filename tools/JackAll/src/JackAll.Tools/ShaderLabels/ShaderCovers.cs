namespace JackAll.Tools.ShaderLabels;

/// <summary>What a shader draws, as a label category, by its source name.</summary>
public static class ShaderCovers
{
    public const string Shared = "shared";

    private static readonly Dictionary<string, string> ByShader = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mesh_grass"] = "foliage", ["aaaleaf"] = "foliage", ["mesh_bigleaf"] = "foliage", ["realtreetrunk"] = "foliage",
        ["terrain"] = "ground", ["terrainlayercompositing"] = "ground", ["burnterrain"] = "ground",
        ["faketerrain"] = "ground", ["road"] = "ground", ["decal"] = "ground", ["terrainskyocclusion"] = "ground",
        ["mesh_weapon"] = "weapon",
        ["mesh_skin"] = "characters", ["mesh_hair"] = "characters", ["mesh_cloth"] = "characters",
        ["mesh_vehicle"] = "vehicles",
        ["aaa"] = "world meshes", ["mesh_procedural"] = "world meshes", ["mesh_unlit"] = "world meshes",
        ["mesh_highlight"] = "world meshes",
        ["skydome"] = "sky", ["skydisk"] = "sky", ["cloudlayer"] = "sky", ["cloudnoiseblur"] = "sky",
        ["cloudnoisecombine"] = "sky", ["celestialbody"] = "sky", ["starsphere"] = "sky",
        ["water"] = "water", ["waterriver"] = "water", ["watercameraplane"] = "water", ["waterskyocclusion"] = "water",
        ["sectorwatervisibilitytest"] = "water",
        ["particle"] = "effects", ["resolve"] = "effects", ["d3d10resolve"] = "effects",
    };

    /// <summary>Post effects are "effects"; anything the table does not name is engine plumbing.</summary>
    public static string Of(string shader)
        => ByShader.TryGetValue(shader, out string? category) ? category
            : shader.StartsWith("posteffect_", StringComparison.OrdinalIgnoreCase) ? "effects"
            : "engine";
}
