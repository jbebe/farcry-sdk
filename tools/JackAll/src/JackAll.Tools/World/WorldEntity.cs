using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// One placed entity in the live editing pool. <see cref="Position"/>/<see cref="Angles"/> are the
/// values being edited; <see cref="Node"/>'s own hidPos/hidAngles bytes stay untouched until a save
/// rebuilds the owning sector, so the pristine tree remains authoritative for everything else.
/// </summary>
public sealed class WorldEntity
{
    public required FcbObject Node { get; init; }
    public required WorldSectorDocument HomeSector { get; set; }

    /// <summary>The owning MissionLayer's text_PathId, e.g. "main" - where a rebuild re-files this entity.</summary>
    public required string LayerPathId { get; init; }

    /// <summary>disEntityId; the stable identity mission scripts reference entities by.</summary>
    public ulong Id { get; init; }

    /// <summary>hidName, falling back to tplCreatureType when unnamed.</summary>
    public string Name { get; init; } = "";

    /// <summary>The archetype this entity instantiates (tplCreatureType), empty when absent.</summary>
    public string ArchetypeName { get; init; } = "";

    /// <summary>Global world position - worldsector files store global coordinates, no cell offset
    /// applies. Null for the rare entity that carries neither hidPos nor hidPos_precise; it stays in
    /// the pool (a rebuild must not drop it) but cannot be shown or moved.</summary>
    public Vector3? Position { get; set; }

    public Vector3 Angles { get; set; }

    /// <summary>The orientation <see cref="Angles"/> means, as the engine composes it: ZXY Euler in
    /// degrees, yaw applied last. The one definition of the convention - the map's vertex shader
    /// mirrors this and nothing else should re-derive it.</summary>
    public Matrix4x4 Rotation
    {
        get
        {
            Vector3 radians = Angles * (MathF.PI / 180f);
            return Matrix4x4.CreateRotationY(radians.Y)
                * Matrix4x4.CreateRotationX(radians.X)
                * Matrix4x4.CreateRotationZ(radians.Z);
        }
    }

    /// <summary>Placed this session, i.e. absent from <see cref="WorldSectorDocument.PristineRoot"/>.</summary>
    public bool IsNew { get; set; }
}
