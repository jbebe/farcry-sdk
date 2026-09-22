using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// One proximity trigger box: the volume that fires when something enters it.
/// <paramref name="Size"/> is <c>vectorSize</c> straight from the file and <paramref name="Yaw"/> the
/// entity's rotation about Z, in degrees.
/// </summary>
public sealed record TriggerVolume(string Name, Vector3 Position, Vector3 Size, float Yaw, bool Enabled)
{
    /// <summary>
    /// The box's eight corners, in world space, rotated about its own centre.
    /// </summary>
    /// <remarks>
    /// <c>vectorSize</c> is read as the box's full extent, so the half-extent is half of it, and the
    /// box is centred on the entity. Neither is confirmed from the engine - the containment test
    /// happens in physics registration rather than in <c>IsInside</c>, which only checks a
    /// membership list. Both assumptions are visible the moment a box is drawn around a building.
    /// </remarks>
    public Vector3[] Corners()
    {
        Vector3 half = Size * 0.5f;
        Vector3[] axes = Axes(Yaw);

        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            // Bit per axis: 1 = the high side of the box.
            float x = (i & 1) == 0 ? -half.X : half.X;
            float y = (i & 2) == 0 ? -half.Y : half.Y;
            float z = (i & 4) == 0 ? -half.Z : half.Z;
            corners[i] = Position + (axes[0] * x) + (axes[1] * y) + (axes[2] * z);
        }
        return corners;
    }

    /// <summary>The box's own X, Y and Z in the world, for a yaw in degrees.</summary>
    public static Vector3[] Axes(float yaw)
    {
        (float sin, float cos) = MathF.SinCos(yaw * MathF.PI / 180f);
        return [new(cos, sin, 0f), new(-sin, cos, 0f), Vector3.UnitZ];
    }
}

/// <summary>
/// The trigger volumes a world places. Like the lights these are components on ordinary sector
/// entities rather than a file of their own, so this reads the loaded entity pool.
/// </summary>
/// <remarks>
/// Only <c>CProximityTriggerComponent</c> carries geometry. The other trigger components
/// (time-of-day, delay, look-at) fire on their own conditions and have nothing to draw.
/// </remarks>
public static class WorldTriggers
{
    private static readonly uint Enabled = FcbClassDefinitions.Crc32Ascii("bEnabled");

    /// <param name="nodeOf">The node to read each entity from, so unsaved edits show; the loaded one by default.</param>
    public static IReadOnlyList<TriggerVolume> Load(IEnumerable<WorldEntity> entities, Func<WorldEntity, FcbObject>? nodeOf = null)
    {
        var triggers = new List<TriggerVolume>();
        foreach (WorldEntity entity in entities)
        {
            if (entity.Position is not { } position ||
                FcbEntityFields.FindComponent(nodeOf?.Invoke(entity) ?? entity.Node, WorldHashes.CProximityTriggerComponent) is not { } trigger ||
                FcbEntityFields.ReadVector3(trigger, WorldHashes.VectorSize) is not { } size)
            {
                continue;
            }

            triggers.Add(new TriggerVolume(
                entity.Name, position, size, entity.Angles.Z, FcbEntityFields.ReadBool(trigger, Enabled)));
        }
        return triggers;
    }

    /// <summary>The full extent of the entity's trigger box, or null when it has none.</summary>
    public static Vector3? SizeOf(FcbObject node)
        => FcbEntityFields.FindComponent(node, WorldHashes.CProximityTriggerComponent) is { } trigger
            ? FcbEntityFields.ReadVector3(trigger, WorldHashes.VectorSize)
            : null;
}
