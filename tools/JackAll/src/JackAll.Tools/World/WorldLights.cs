using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>One placed light. <paramref name="IsSpot"/> separates the cone lights from the omni
/// (point) ones, which are the bulk of them.</summary>
public sealed record WorldLight(
    string Name, Vector3 Position, Vector3 Colour, float Radius, float Intensity, bool IsSpot,
    bool Enabled);

/// <summary>
/// The lights a world places. They are not a file of their own: a light is a
/// <c>CDynamicLightComponent</c> hanging off an ordinary entity in the sector data, so this reads the
/// entity pool that is already loaded rather than touching the archives again.
/// </summary>
/// <remarks>
/// Despite the name, <c>&lt;world&gt;.omnis.fcb</c> holds no lights - "omni" there means omnipresent,
/// world-scope entities outside the sector grid (in retail, five DLC Domino hosts in world1).
/// </remarks>
public static class WorldLights
{
    private static readonly uint DynamicLight = FcbClassDefinitions.Crc32Ascii("CDynamicLightComponent");
    private static readonly uint Type = FcbClassDefinitions.Crc32Ascii("hidType");
    private static readonly uint Radius = FcbClassDefinitions.Crc32Ascii("fRadius");
    private static readonly uint Colour = FcbClassDefinitions.Crc32Ascii("clrColor");
    private static readonly uint Intensity = FcbClassDefinitions.Crc32Ascii("fIntensity");
    private static readonly uint Enabled = FcbClassDefinitions.Crc32Ascii("bEnabled");

    /// <summary>hidType 1 is an omni light; 3 is a spot, which additionally carries the two angles.</summary>
    private const uint SpotType = 3;

    public static IReadOnlyList<WorldLight> Load(IEnumerable<WorldEntity> entities)
    {
        var lights = new List<WorldLight>();
        foreach (WorldEntity entity in entities)
        {
            if (entity.Position is not { } position || Find(entity.Node) is not { } light)
            {
                continue;
            }

            lights.Add(new WorldLight(
                entity.Name,
                position,
                Vec3(light, Colour) ?? Vector3.One,
                Scalar(light, Radius) ?? 0f,
                Scalar(light, Intensity) ?? 1f,
                Word(light, Type) == SpotType,
                light.Values.TryGetValue(Enabled, out byte[]? on) && on.Length > 0 && on[0] != 0));
        }
        return lights;
    }

    private static FcbObject? Find(FcbObject entity)
    {
        foreach (FcbObject group in entity.Children)
        {
            if (group.TypeHash != WorldHashes.Components)
            {
                continue;
            }
            foreach (FcbObject component in group.Children)
            {
                if (component.TypeHash == DynamicLight)
                {
                    return component;
                }
            }
        }
        return null;
    }

    private static Vector3? Vec3(FcbObject node, uint field) =>
        node.Values.TryGetValue(field, out byte[]? raw) && raw.Length == 12
            ? new Vector3(
                BitConverter.ToSingle(raw, 0), BitConverter.ToSingle(raw, 4), BitConverter.ToSingle(raw, 8))
            : null;

    private static float? Scalar(FcbObject node, uint field) =>
        node.Values.TryGetValue(field, out byte[]? raw) && raw.Length == 4
            ? BitConverter.ToSingle(raw, 0)
            : null;

    private static uint? Word(FcbObject node, uint field) =>
        node.Values.TryGetValue(field, out byte[]? raw) && raw.Length == 4
            ? BitConverter.ToUInt32(raw, 0)
            : null;
}
