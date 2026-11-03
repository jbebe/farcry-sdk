using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Tools.Sdat;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.World;

/// <summary>
/// A new world's vegetation as its author lays it out, grown into each sector's landmark files when
/// the world is packed: meshes into its near file, RealTrees into its far one, as shipped levels keep
/// them. A patch spreads one mesh or tree over rectangles at an even spacing and at single spots,
/// every copy turned at random and dressed as a random copy of the same one in a shipped level, its
/// donor:
/// <code>
/// &lt;Vegetation&gt;
///   &lt;Patch Mesh="graphics\vegetation\savannah\1_grasssavannah_b.xbg" Donor="w1_b_2" Spacing="1.6" Seed="1"&gt;
///     &lt;Area X="192" Y="320" Width="128" Height="64" /&gt;
///   &lt;/Patch&gt;
///   &lt;Patch Mesh="graphics\vegetation\savannah\realtrees\rt_tree_acacia.rtx" Donor="w1_b_2"&gt;
///     &lt;At X="150" Y="350" /&gt;
///   &lt;/Patch&gt;
/// &lt;/Vegetation&gt;
/// </code>
/// </summary>
public static class VegetationPlan
{
    /// <summary>The side of the square each cluster gathers its tufts from, as the editor lays them.</summary>
    public const float ClusterSide = 16;

    /// <summary>The spacing of a shipped savannah field: about 96 tufts to a cluster's square.</summary>
    public const float DefaultSpacing = 1.6f;

    /// <summary>The <c>playerCoverage</c> from which <c>CStealthComponent::InHighGrass</c> counts a mesh as hiding a crouched player.</summary>
    public const float HighGrass = 1;

    /// <summary>Above every retail id and the Map tab's own; a sector's entity adds its sector id.</summary>
    private const ulong SectorEntityIdFloor = 0x4100_0000_0000_0000;

    /// <summary>What a far file's sector entity adds besides, so it never shares the near one's id.</summary>
    private const ulong FarEntityIdOffset = 0x1_0000;

    /// <summary>The collection manager's <c>classId</c> of a RealTree; a mesh's is 1.</summary>
    private const int RealTreeClass = 2;

    private static readonly uint CSectorEntity = Hash("CSectorEntity");
    private static readonly uint Resource = Hash("Resource");
    private static readonly uint ClassId = Hash("classId");
    private static readonly uint FileNameId = Hash("fileNameId");
    private static readonly uint PlayerCoverage = Hash("playerCoverage");

    public static string PathOf(string level) => $@"levels\{level}\vegetation.xml";

    /// <summary>The landmark file of every sector the plan grows something in, by sector id and whether it is the far one.</summary>
    public static Dictionary<(int Sector, bool Far), byte[]> Build(
        XElement plan, TerrainMap map, Func<string, byte[]?> read, IEnumerable<string> knownPaths,
        IProgress<string>? progress = null)
    {
        string[] paths = [.. knownPaths];
        Dictionary<uint, (bool Tree, float Covers)> listed = Listed(map.Name, read);
        var ground = new Ground(map, read);
        var looks = new Dictionary<string, ILookup<uint, (uint Colour, uint Normal, uint Sky)>>(StringComparer.OrdinalIgnoreCase);
        var boxes = new Dictionary<uint, (Vector3 Min, Vector3 Max)>();
        var grown = new List<(int Sector, uint Resource, bool Tree, VegetationTuft Tuft)>();
        foreach (XElement patch in plan.Elements("Patch"))
        {
            string mesh = NameHash.Normalize(Required(patch, "Mesh"));
            uint resource = NameHash.Compute(mesh);
            if (!listed.TryGetValue(resource, out (bool Tree, float Covers) entry))
            {
                throw new InvalidOperationException($"{map.Name}'s collection manager has no {mesh}, so the game would not draw it.");
            }
            if (!entry.Tree && entry.Covers < HighGrass)
            {
                progress?.Report($"{mesh} hides no one from the AI: its playerCoverage is {entry.Covers}");
            }
            if (!boxes.ContainsKey(resource))
            {
                byte[] bytes = read(mesh) ?? throw new InvalidOperationException($"{mesh} is not in the game.");
                boxes[resource] = entry.Tree ? XbgModel.Bounds(WorldModels.Triangulate(mesh, bytes).Submeshes) : Box(XbgFile.Parse(bytes).Box);
            }

            string donor = Required(patch, "Donor");
            (uint Colour, uint Normal, uint Sky)[] dressed =
                [.. (looks.TryGetValue(donor, out var lookup) ? lookup : looks[donor] = Looks(donor, read, paths))[resource]];
            if (dressed.Length == 0)
            {
                throw new InvalidOperationException($"{donor} places no {mesh} to take its look from.");
            }

            float spacing = Number(patch, "Spacing", DefaultSpacing);
            var random = new Random((int)Number(patch, "Seed", 0));

            // A tree keeps its donor's words whole: the ground's colour stands where a mesh keeps its normal.
            void Plant(Vector2 at)
            {
                int sector = ground.SectorAt(at)
                    ?? throw new InvalidOperationException($"A {mesh} reaches past {map.Name} at ({at.X}, {at.Y}).");
                (float height, Vector3 normal) = ground.At(sector, at);
                (uint colour, uint underneath, uint sky) = dressed[random.Next(dressed.Length)];
                float turn = random.NextSingle() * MathF.Tau;
                (uint orientation, uint second) = entry.Tree
                    ? (VegetationTuft.PackTreeTurn(turn), underneath)
                    : (VegetationTuft.PackTurn(turn), VegetationTuft.PackNormal(normal));
                grown.Add((sector, resource, entry.Tree, new VegetationTuft(new Vector3(at, height), orientation, colour, second, sky)));
            }

            foreach (XElement area in patch.Elements("Area"))
            {
                float x0 = Number(area, "X"), y0 = Number(area, "Y");
                float x1 = x0 + Number(area, "Width"), y1 = y0 + Number(area, "Height");
                for (float y = y0; y < y1; y += spacing)
                {
                    for (float x = x0; x < x1; x += spacing)
                    {
                        // One tuft somewhere in each spacing's square, on the decimetre the file keeps.
                        var at = new Vector2(
                            VegetationTuft.OnDecimetre(x + random.NextSingle() * spacing),
                            VegetationTuft.OnDecimetre(y + random.NextSingle() * spacing));
                        if (at.X < x1 && at.Y < y1)
                        {
                            Plant(at);
                        }
                    }
                }
            }
            foreach (XElement spot in patch.Elements("At"))
            {
                Plant(new Vector2(VegetationTuft.OnDecimetre(Number(spot, "X")), VegetationTuft.OnDecimetre(Number(spot, "Y"))));
            }
        }

        // Trees go to the far file, so a file's trees flag is its far flag too.
        return grown.GroupBy(t => (t.Sector, Far: t.Tree)).ToDictionary(file => file.Key, file =>
        {
            bool trees = file.Key.Far;
            VegetationResource[] resources = [.. file
                .GroupBy(t => t.Resource, t => t.Tuft)
                .Select(r => new VegetationResource(r.Key, Clusters(r, trees ? null : boxes[r.Key])))];
            (Vector3 min, Vector3 max) = XbgModel.Bounds(file.SelectMany(t => Corners(t.Tuft, trees ? t.Tuft.TreeTurn : t.Tuft.Turn, boxes[t.Resource])));
            return FcbDocument.Serialize(Landmark(file.Key.Sector, map.SectorsPerSide, trees, new VegetationZone(0, resources), min, max));
        });
    }

    /// <summary>
    /// Each mesh and RealTree the world's collection manager lists, by path hash, with its
    /// <c>playerCoverage</c>. The game spawns no copy of one it does not list.
    /// </summary>
    private static Dictionary<uint, (bool Tree, float Covers)> Listed(string world, Func<string, byte[]?> read)
    {
        string path = $@"worlds\{world}\generated\{world}.managers.fcb";
        FcbObject managers = FcbDocument.Deserialize(read(path) ?? throw new InvalidOperationException($"{path} is missing."));
        return managers.Descendants()
            .Where(n => n.TypeHash == Resource && FcbEntityFields.ReadU32(n, FileNameId) is not null)
            .DistinctBy(n => FcbEntityFields.ReadU32(n, FileNameId))
            .ToDictionary(
                n => FcbEntityFields.ReadU32(n, FileNameId)!.Value,
                n => (FcbEntityFields.ReadU32(n, ClassId) == RealTreeClass, FcbEntityFields.ReadFloat(n, PlayerCoverage) ?? 0));
    }

    private static (Vector3 Min, Vector3 Max) Box(float[] box) => (new Vector3(box[0], box[1], box[2]), new Vector3(box[3], box[4], box[5]));

    /// <summary>The three colour words of every copy in the donor level's landmark files, by mesh.</summary>
    private static ILookup<uint, (uint Colour, uint Normal, uint Sky)> Looks(string donor, Func<string, byte[]?> read, string[] paths)
    {
        string prefix = $@"levels\{donor}\generated\worldsectors\landmark";
        return paths
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(read)
            .OfType<byte[]>()
            .Select(FcbDocument.TryDeserialize)
            .OfType<FcbObject>()
            .SelectMany(WorldVegetation.Zones)
            .SelectMany(z => z.Resources)
            .SelectMany(r => r.Clusters.SelectMany(c => c.Tufts), (r, t) => (r.Id, Look: (t.Colour, t.Normal, t.Sky)))
            .ToLookup(t => t.Id, t => t.Look);
    }

    /// <summary>
    /// The tufts gathered into clusters by the square they stand in, none over a cluster's limit. A
    /// mesh's cluster is culled by the sphere <c>CSceneGraphicObjectClusterHelper::UpdateBoundingVolume</c>
    /// fits from its <paramref name="box"/>; a RealTree's, given none, is saved without one, as
    /// <c>StClusterData::StoreRealtreeCluster</c> saves it.
    /// </summary>
    private static VegetationCluster[] Clusters(IEnumerable<VegetationTuft> tufts, (Vector3 Min, Vector3 Max)? box)
        => [.. tufts
            .GroupBy(t => ((int)MathF.Floor(t.Position.Y / ClusterSide), (int)MathF.Floor(t.Position.X / ClusterSide)))
            .OrderBy(g => g.Key)
            .SelectMany(g => g.Chunk(VegetationCluster.MaxTufts))
            .Select(chunk => new VegetationCluster(box is { } mesh ? Sphere(chunk, mesh) : Vector4.Zero, chunk))];

    /// <summary>
    /// Each tuft as a sphere at its mesh box's centre with the box's shortest side as radius, as
    /// <c>GetGeometryLocalBSphere</c> makes it; the cluster's sphere sits at the middle of their
    /// bounds, its radius their longest side.
    /// </summary>
    private static Vector4 Sphere(VegetationTuft[] tufts, (Vector3 Min, Vector3 Max) box)
    {
        Vector3 centre = (box.Min + box.Max) / 2, side = box.Max - box.Min;
        var radius = new Vector3(MathF.Min(side.X, MathF.Min(side.Y, side.Z)));
        (Vector3 min, Vector3 max) = XbgModel.Bounds(tufts.SelectMany(t =>
        {
            Vector3 at = t.Position + Rotate(centre, MathF.SinCos(t.Turn));
            return new[] { at - radius, at + radius };
        }));
        Vector3 size = max - min;
        return new Vector4((min + max) / 2, MathF.Max(size.X, MathF.Max(size.Y, size.Z)));
    }

    /// <summary>The corners of a tuft's turned mesh box, whose bounds over a sector <c>VegetationData</c> carries.</summary>
    private static IEnumerable<Vector3> Corners(VegetationTuft tuft, float radians, (Vector3 Min, Vector3 Max) box)
    {
        (float Sin, float Cos) turn = MathF.SinCos(radians);
        foreach ((float x, float y) in new[] { (box.Min.X, box.Min.Y), (box.Max.X, box.Min.Y), (box.Min.X, box.Max.Y), (box.Max.X, box.Max.Y) })
        {
            Vector3 at = tuft.Position + Rotate(new Vector3(x, y, 0), turn);
            yield return at with { Z = tuft.Position.Z + box.Min.Z };
            yield return at with { Z = tuft.Position.Z + box.Max.Z };
        }
    }

    private static Vector3 Rotate(Vector3 v, (float Sin, float Cos) turn)
        => new(v.X * turn.Cos - v.Y * turn.Sin, v.X * turn.Sin + v.Y * turn.Cos, v.Z);

    /// <summary>A sector's near or far landmark file, shaped as a shipped one less its fire.</summary>
    private static FcbObject Landmark(int sectorId, int sectorsPerSide, bool far, VegetationZone zone, Vector3 min, Vector3 max)
    {
        (string category, string name, ulong id) = far
            ? ("LandmarkFar", $"SectorEntityFar_{sectorId}", SectorEntityIdFloor + FarEntityIdOffset + (ulong)sectorId)
            : ("LandmarkNear", $"SectorEntity_{sectorId}", SectorEntityIdFloor + (ulong)sectorId);
        var data = new FcbObject { TypeHash = Hash("VegetationData") };
        data.Values[Hash("dataVersion")] = BitConverter.GetBytes(5);
        data.Values[Hash("bboxMin")] = FcbEntityFields.Vector3Bytes(min);
        data.Values[Hash("bboxMax")] = FcbEntityFields.Vector3Bytes(max);
        data.Children.Add(zone.ToFcb());
        FcbObject collection = Component(Hash("CCollectionComponent"));
        collection.Children.Add(data);

        FcbObject persist = Component(Hash("CPersistComponent"));
        persist.Values[Hash("selLevel")] = BitConverter.GetBytes(0u);
        var levels = new FcbObject { TypeHash = Hash("enumLevel") };
        foreach (string level in new[] { "None", "Limited", "Full", "Critical" })
        {
            var option = new FcbObject { TypeHash = Hash("enum") };
            option.Values[Hash("Value")] = FcbEntityFields.StringBytes(level);
            levels.Children.Add(option);
        }
        persist.Children.Add(levels);

        FcbObject mission = Component(WorldHashes.CMissionComponent);
        mission.Values[Hash("text_hidMissionLayerPath")] = FcbEntityFields.StringBytes("");
        mission.Values[WorldHashes.HidMissionLayerPath] = BitConverter.GetBytes(uint.MaxValue);
        mission.Values[Hash("text_hidCategory")] = FcbEntityFields.StringBytes(category);
        mission.Values[Hash("hidCategory")] = BitConverter.GetBytes(Hash(category));
        mission.Values[Hash("ForceMerge")] = [1];

        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.AddRange([collection, persist, mission, EntityLinks.NewEventComponent()]);

        int x = sectorId % sectorsPerSide, y = sectorId / sectorsPerSide;
        const float half = WorldModels.SectorMeters / 2;
        byte[] at = FcbEntityFields.Vector3Bytes(new Vector3(x * WorldModels.SectorMeters + half, y * WorldModels.SectorMeters + half, 0));
        var entity = new FcbObject { TypeHash = WorldHashes.Entity };
        entity.Values[WorldHashes.HidName] = FcbEntityFields.StringBytes(name);
        entity.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(id);
        entity.Values[WorldHashes.TextHidEntityClass] = FcbEntityFields.StringBytes("CSectorEntity");
        entity.Values[WorldHashes.HidEntityClass] = BitConverter.GetBytes(CSectorEntity);
        entity.Values[WorldHashes.HidResourceCount] = BitConverter.GetBytes((uint)zone.Resources.Count);
        entity.Values[WorldHashes.HidPos] = at;
        // As shipped, its Y a negative zero.
        entity.Values[WorldHashes.HidAngles] = FcbEntityFields.Vector3Bytes(new Vector3(0, -0f, 0));
        entity.Values[WorldHashes.HidPosPrecise] = (byte[])at.Clone();
        entity.Values[WorldHashes.HidConstEntity] = [1];
        entity.Children.Add(components);

        FcbObject sector = ContainerSkeletons.WorldSector(sectorId);
        sector.Values[WorldHashes.WorldSectorX] = BitConverter.GetBytes((uint)x);
        sector.Values[WorldHashes.WorldSectorY] = BitConverter.GetBytes((uint)y);
        sector.Children.Single().Children.Add(entity);
        return sector;
    }

    private static FcbObject Component(uint type)
    {
        var component = new FcbObject { TypeHash = type };
        component.Values[WorldHashes.HidHasAliasName] = [0];
        return component;
    }

    private static string Required(XElement element, string attribute)
        => (string?)element.Attribute(attribute)
           ?? throw new InvalidOperationException($"A {element.Name} needs {attribute}.");

    private static float Number(XElement element, string attribute, float? fallback = null)
        => element.Attribute(attribute) is { } value
            ? float.Parse(value.Value, CultureInfo.InvariantCulture)
            : fallback ?? throw new InvalidOperationException($"A {element.Name} needs {attribute}.");

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

    /// <summary>The terrain the plan stands its tufts on, each sector decoded once it is reached.</summary>
    private sealed class Ground(TerrainMap map, Func<string, byte[]?> read)
    {
        private readonly Dictionary<int, string> _paths = map.Sectors.ToDictionary(s => s.SectorId, s => s.Path);
        private readonly Dictionary<int, SdatSector> _sectors = [];

        /// <summary>The sector a point stands in, or null off the map.</summary>
        public int? SectorAt(Vector2 at)
        {
            (int x, int y) = WorldModels.SectorOf(new Vector3(at, 0));
            int id = y * map.SectorsPerSide + x;
            return x >= 0 && y >= 0 && x < map.SectorsPerSide && _paths.ContainsKey(id) ? id : null;
        }

        /// <summary>
        /// The height blended from the four samples around the point, and the nearest sample's normal
        /// decoded as <c>CSector::GetNormal</c> does: Z is <c>1 - sqrt(x*x + y*y)</c>, not unit length.
        /// </summary>
        public (float Height, Vector3 Normal) At(int id, Vector2 at)
        {
            SdatSector sector = _sectors.TryGetValue(id, out SdatSector? decoded)
                ? decoded
                : _sectors[id] = SdatSectorFile.Decode(read(_paths[id]) ?? throw new InvalidOperationException($"{_paths[id]} is missing."));
            float x = at.X - id % map.SectorsPerSide * WorldModels.SectorMeters;
            float y = at.Y - id / map.SectorsPerSide * WorldModels.SectorMeters;
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            float Height(int row, int column) => sector.Grid[row, column].HeightMeters;
            float height = (Height(y0, x0) * (1 - fx) + Height(y0, x0 + 1) * fx) * (1 - fy)
                           + (Height(y0 + 1, x0) * (1 - fx) + Height(y0 + 1, x0 + 1) * fx) * fy;

            int nearRow = (int)MathF.Round(y), nearColumn = (int)MathF.Round(x);
            float nx = sector.Grid[nearRow, nearColumn].NormalX / 255f * 2 - 1;
            float ny = sector.NormalYAt(nearRow, nearColumn) / 255f * 2 - 1;
            return (height, new Vector3(nx, ny, 1 - MathF.Sqrt(nx * nx + ny * ny)));
        }
    }
}
