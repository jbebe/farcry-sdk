using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Format;

/// <summary>
/// The resource classes a `depload.dat` child can be tagged with, and the hash rule behind them.
/// </summary>
/// <remarks>
/// A child's type hash is the CRC32 of its class name hashed exact-case - not lowercased, unlike a
/// <see cref="JackAll.Core.Format.NameHash"/> path. These sixteen are every distinct value across the
/// 27 shipped files; each name is confirmed by hashing to the value observed in a real type table,
/// and the shipped `_depload.xml` twins spell most of them out in their own `crc_Type` attributes.
/// </remarks>
public static class DepLoadTypes
{
    private static readonly string[] Known =
    [
        "CAnimationPackageResource",
        "CAnimationResource",
        "CDominoBoxResource",
        "CFaceAnimResource",
        "CFrankensteinPoseResource",
        "CGeometryResource",
        "CMaterialResource",
        "CMovementResource",
        "CParticlesEmitterParamResource",
        "CParticlesSystemParamResource",
        "CPhysResource",
        "CResourceContainer",
        "CSkeletonResource",
        "CSoundResource",
        "CStateMachineResource",
        "CTextureResource",
    ];

    private static readonly Dictionary<uint, string> NamesByHash =
        Known.ToDictionary(Hash, name => name);

    /// <summary>The class name a `depload` type hash stands for, or null when it is not one of the known ones.</summary>
    public static string? NameOf(uint typeHash) => NamesByHash.GetValueOrDefault(typeHash);

    /// <summary>The same exact-case name hash `.fcb` class and field names use.</summary>
    public static uint Hash(string className) => FcbClassDefinitions.Crc32Ascii(className);
}
