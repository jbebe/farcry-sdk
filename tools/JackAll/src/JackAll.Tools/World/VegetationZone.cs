using System.Numerics;
using System.Runtime.InteropServices;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// One placed copy of a scatter mesh, its fields as the engine keeps them. See
/// docs/docs/engine-internals/terrain-and-vegetation.md#one-tuft.
/// </summary>
/// <param name="Position">X and Y are stored in decimetres, so they come back on that step.</param>
/// <param name="Orientation">A mesh's turn about Z: its sine in the high <c>i16</c>, its cosine in the low, each x 32767. A RealTree's is hundredths of a degree.</param>
/// <param name="Colour">RGB the ground's colour under it, A its height scale. A RealTree's is <c>0xFFFFFFFF</c>.</param>
/// <param name="Normal">The ground's normal under it, each axis x 0.5 + 0.5 in R, G and B. A RealTree holds the ground's colour here.</param>
/// <param name="Sky">RGB how much sky it sees.</param>
public readonly record struct VegetationTuft(Vector3 Position, uint Orientation, uint Colour, uint Normal, uint Sky)
{
    /// <summary>The <see cref="Orientation"/> of a mesh turned by <paramref name="radians"/>, as the editor packs it.</summary>
    public static uint PackTurn(float radians)
        => (uint)(ushort)(short)(MathF.Sin(radians) * 32767f) << 16 | (ushort)(short)(MathF.Cos(radians) * 32767f);

    /// <summary>The <see cref="Orientation"/> of a RealTree turned by <paramref name="radians"/>, as <c>GetPackedRealtreeClusterOrientData</c> packs it.</summary>
    public static uint PackTreeTurn(float radians) => (uint)(int)Math.Round(radians * (180 / Math.PI) / 0.01);

    /// <summary>The turn a mesh's <see cref="Orientation"/> holds, in radians.</summary>
    public float Turn => MathF.Atan2((short)(Orientation >> 16) / 32767f, (short)Orientation / 32767f);

    /// <summary>The turn a RealTree's <see cref="Orientation"/> holds, in radians.</summary>
    public float TreeTurn => (int)Orientation * 0.01f * (MathF.PI / 180);

    /// <summary>A <see cref="Normal"/> as the editor packs one.</summary>
    public static uint PackNormal(Vector3 normal)
        => (uint)(Unit(normal.X) << 16 | Unit(normal.Y) << 8 | Unit(normal.Z));

    /// <summary>A coordinate on the decimetre the lists keep it on.</summary>
    public static float OnDecimetre(float metres) => (float)(Decimetres(metres) / 10.0);

    /// <summary>How the editor quantizes a coordinate, rounding half to even as its <c>FISTP</c> does.</summary>
    internal static uint Decimetres(float metres) => (uint)Math.Round(metres * 10.0) & 0xFFFF;

    private static int Unit(float value) => Math.Clamp((int)((value * 0.5f + 0.5f) * 255f), 0, 255);
}

/// <summary>A run of tufts drawn as one batch, with the sphere it is culled by.</summary>
public sealed record VegetationCluster(Vector4 Sphere, IReadOnlyList<VegetationTuft> Tufts)
{
    /// <summary>The most tufts one cluster can hold: its count is a byte.</summary>
    public const int MaxTufts = 255;
}

/// <summary>Every cluster of one mesh, which <see cref="Id"/> names by the CRC32 of its path.</summary>
public sealed record VegetationResource(uint Id, IReadOnlyList<VegetationCluster> Clusters);

/// <summary>
/// A <c>VegetationZoneData</c>: nine parallel lists, each a <c>u32</c> count and that many 4-byte
/// elements, written and read here as the resources, clusters and tufts they flatten.
/// </summary>
public sealed record VegetationZone(ulong ZoneId, IReadOnlyList<VegetationResource> Resources)
{
    public static readonly uint TypeHash = FcbClassDefinitions.Crc32Ascii("VegetationZoneData");

    private static readonly uint ZoneIdField = Hash("zoneId");
    private static readonly uint ResourceList = Hash("resourceList");
    private static readonly uint ResClustersCntList = Hash("resClustersCntList");
    private static readonly uint BoundingVolumeList = Hash("boundingVolumeList");
    private static readonly uint ClustersInstancesCntList = Hash("clustersInstancesCntList");
    private static readonly uint PosXYList = Hash("posXYList");
    private static readonly uint PosZList = Hash("posZList");
    private static readonly uint OrientZDataList = Hash("orientZDataList");
    private static readonly uint ColorsList = Hash("colorsList");

    public static VegetationZone Read(FcbObject zone)
    {
        uint[] resources = List<uint>(zone, ResourceList);
        uint[] clustersPerResource = List<uint>(zone, ResClustersCntList);
        float[] spheres = List<float>(zone, BoundingVolumeList);
        uint[] clusters = List<uint>(zone, ClustersInstancesCntList);
        uint[] xy = List<uint>(zone, PosXYList);
        float[] z = List<float>(zone, PosZList);
        uint[] orientations = List<uint>(zone, OrientZDataList);
        uint[] colours = List<uint>(zone, ColorsList);

        var read = new List<VegetationResource>(resources.Length);
        int cluster = 0;
        for (int resource = 0; resource < resources.Length; resource++)
        {
            var owned = new List<VegetationCluster>((int)clustersPerResource[resource]);
            for (uint n = 0; n < clustersPerResource[resource]; n++, cluster++)
            {
                int count = (int)(clusters[cluster] & 0xFF), first = (int)(clusters[cluster] >> 8);
                var tufts = new VegetationTuft[count];
                for (int i = 0; i < count; i++)
                {
                    int t = first + i;
                    tufts[i] = new VegetationTuft(
                        new Vector3((xy[t] & 0xFFFF) / 10f, (xy[t] >> 16) / 10f, z[t]),
                        orientations[t], colours[t * 3], colours[t * 3 + 1], colours[t * 3 + 2]);
                }
                owned.Add(new VegetationCluster(new Vector4(spheres.AsSpan(cluster * 4, 4)), tufts));
            }
            read.Add(new VegetationResource(resources[resource], owned));
        }
        return new VegetationZone(FcbEntityFields.ReadU64(zone, ZoneIdField), read);
    }

    public FcbObject ToFcb()
    {
        VegetationCluster[] clusters = [.. Resources.SelectMany(r => r.Clusters)];
        VegetationTuft[] tufts = [.. clusters.SelectMany(c => c.Tufts)];
        var packed = new uint[clusters.Length];
        for (int i = 0, first = 0; i < clusters.Length; first += clusters[i].Tufts.Count, i++)
        {
            packed[i] = (uint)first << 8 | (uint)clusters[i].Tufts.Count;
        }
        Vector4[] spheres = [.. clusters.Select(c => c.Sphere)];

        var zone = new FcbObject { TypeHash = TypeHash };
        zone.Values[ZoneIdField] = BitConverter.GetBytes(ZoneId);
        zone.Values[ResourceList] = Uints(Resources.Select(r => r.Id));
        zone.Values[ResClustersCntList] = Uints(Resources.Select(r => (uint)r.Clusters.Count));
        zone.Values[BoundingVolumeList] = FcbValueCodec.Encode(FcbMemberType.FloatArray, MemoryMarshal.Cast<Vector4, float>(spheres).ToArray());
        zone.Values[ClustersInstancesCntList] = Uints(packed);
        zone.Values[PosXYList] = Uints(tufts.Select(t => VegetationTuft.Decimetres(t.Position.Y) << 16 | VegetationTuft.Decimetres(t.Position.X)));
        zone.Values[PosZList] = FcbValueCodec.Encode(FcbMemberType.FloatArray, tufts.Select(t => t.Position.Z).ToArray());
        zone.Values[OrientZDataList] = Uints(tufts.Select(t => t.Orientation));
        zone.Values[ColorsList] = Uints(tufts.SelectMany(t => new[] { t.Colour, t.Normal, t.Sky }));
        return zone;
    }

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

    /// <summary>A list's elements; a count past the bytes there are reads only as many as there are.</summary>
    private static T[] List<T>(FcbObject zone, uint field) where T : unmanaged
        => zone.Values.GetValueOrDefault(field) is { Length: >= 4 } raw
            ? MemoryMarshal.Cast<byte, T>(raw.AsSpan(4))[..Math.Min(BitConverter.ToInt32(raw), (raw.Length - 4) / 4)].ToArray()
            : [];

    private static byte[] Uints(IEnumerable<uint> values) => FcbValueCodec.Encode(FcbMemberType.UInt32Array, values.ToArray());
}
