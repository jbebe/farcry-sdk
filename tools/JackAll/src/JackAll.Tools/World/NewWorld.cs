using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;

namespace JackAll.Tools.World;

/// <summary>
/// The files of a new world made from <c>tmpla</c>, the editor's flat template: everything the game
/// reads except what <see cref="WorldPack"/> builds from authored content - the worldsectors, the
/// entity library, the sector flags and the navmesh. See docs/docs/modding/building-a-new-world.md.
/// </summary>
public static partial class NewWorld
{
    public const string Template = "tmpla";
    public const int TemplateSide = 8;

    /// <summary>The sizes a world can take. Sector ids are the template's, numbered 8 to a row, so a
    /// grid between would need every sector renumbered, inside its .sdat too.</summary>
    public static IReadOnlyList<int> Sides { get; } = [1, TemplateSide];

    /// <summary>Travel puts the player at the stored position with no ground snap, so the spawn point
    /// sits just above the ground, as retail start points do.</summary>
    private const float SpawnAboveGround = 0.44f;

    private static readonly uint TemplateGroup = FcbClassDefinitions.Crc32Ascii(Template);
    private static readonly uint SectorDesc = FcbClassDefinitions.Crc32Ascii("SectorDesc");
    private static readonly uint DescSector = FcbClassDefinitions.Crc32Ascii("Sector");
    private static readonly uint DescSectorId = 0xBF396750;

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex NamePattern();

    /// <summary>Whether the game itself ships a world named <paramref name="world"/>.</summary>
    public static bool Ships(GameVfs vfs, string world) => vfs.ReadOriginal(NameHash.Compute(WorldDescriptor.PathOf(world))) is not null;

    /// <summary>Whether <paramref name="world"/> is a new one: a layer has its descriptor, and the game does not.</summary>
    public static bool IsNew(GameVfs vfs, string world)
        => !Ships(vfs, world) && vfs.Files.ContainsKey(NameHash.Compute(WorldDescriptor.PathOf(world)));

    /// <summary>Whether <paramref name="name"/> can name a world: lowercase letters, digits and underscores.</summary>
    public static bool IsValidName(string name) => NamePattern().IsMatch(name) && name != Template;

    /// <summary>The template's sector ids a world <paramref name="side"/> sectors wide keeps.</summary>
    public static IReadOnlySet<int> SectorsOf(int side)
        => Enumerable.Range(0, side).SelectMany(y => Enumerable.Range(0, side).Select(x => x + TemplateSide * y)).ToHashSet();

    /// <summary>
    /// Game path -&gt; bytes for world <paramref name="name"/>, <paramref name="side"/> sectors a side,
    /// reading the template through <paramref name="read"/>. The spawn point defaults to the middle of
    /// the plain, on its ground.
    /// </summary>
    public static SortedDictionary<string, byte[]> Create(
        string name, int side, Func<string, byte[]?> read, Vector3? spawn = null)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not a world name: lowercase letters, digits and underscores, starting with a letter.");
        }
        if (!Sides.Contains(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), $"A world is {string.Join(" or ", Sides)} sectors a side.");
        }

        byte[] FromTemplate(string path) => read(path) ?? throw new InvalidOperationException($"{path} is missing; a new world copies it from {Template}.");
        IReadOnlySet<int> sectors = SectorsOf(side);
        var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string srcWorld = $@"worlds\{Template}\";
        string srcLevel = $@"levels\{Template}\";
        string world = $@"worlds\{name}\";
        string level = $@"levels\{name}\";

        foreach (string file in new[] { "mapcompass.xbt", "mapcompass_mip0.xbt", @"generated\moviedata.xml",
            @"generated\tmpla.managers.fcb", @"generated\tmpla.omnis.fcb", @"generated\tmpla_depload.dat",
            @"generated\tmpla_deploadnewparticles.rml" })
        {
            files[world + file.Replace(Template, name)] = FromTemplate(srcWorld + file);
        }

        string descriptor = Encoding.UTF8.GetString(FromTemplate($@"{srcWorld}generated\tmpla.game.xml"));
        string nomad = $@"Levels\{Template}\{Template}.nomad";
        if (descriptor.IndexOf(nomad, StringComparison.Ordinal) is var at && (at < 0 || descriptor.IndexOf(nomad, at + 1, StringComparison.Ordinal) >= 0))
        {
            throw new InvalidDataException($"{Template}.game.xml does not name its map exactly once.");
        }
        descriptor = descriptor.Replace(nomad, $@"Levels\{name}\{name}.nomad")
            .Replace($"CountX=\"{TemplateSide}\" CountY=\"{TemplateSide}\"", $"CountX=\"{side}\" CountY=\"{side}\"");
        files[WorldDescriptor.PathOf(name)] = new UTF8Encoding(false).GetBytes(descriptor);

        FcbObject sectorsDep = FcbDocument.Deserialize(FromTemplate($@"{srcWorld}generated\tmpla.sectorsdep.fcb"));
        RenameGroup(sectorsDep, name);
        SectorsDep.Keep(sectorsDep, sectors);
        files[SectorsDep.PathOf(name)] = FcbDocument.Serialize(sectorsDep);

        files[$@"{world}generated\nv\sectors\nv.nvm"] = NavMeshIndex(FromTemplate($@"{srcWorld}generated\nv\sectors\nv.nvm"), side);

        foreach (string file in new[] { "mapcompass.xbt", "mapcompass_mip0.xbt", @"generated\moviedata.xml" })
        {
            files[level + file] = FromTemplate(srcLevel + file);
        }
        foreach (int id in sectors)
        {
            foreach (string file in new[] { $@"sdat\sd{id}.sdat", $@"sdat\sd{id}_shadow.xbt",
                $@"worldsectors\sector{id}.srl", $@"worldsectors\zonesector{id}.zsr" })
            {
                files[$@"{level}generated\{file}"] = FromTemplate($@"{srcLevel}generated\{file}");
            }

            string desc = $@"generated\worldsectors\sector{id}.desc.fcb";
            files[level + desc] = side == TemplateSide ? FromTemplate(srcLevel + desc) : CutDescriptor(FromTemplate(srcLevel + desc), sectors);
        }
        // One atlas covers a 2 x 2 block of sectors and is named after the block's first sector.
        foreach (int atlas in sectors.Select(s => s - s % 2 - TemplateSide * (s / TemplateSide % 2)).Distinct())
        {
            foreach (string kind in new[] { "color", "diffuse", "mask" })
            {
                string file = $@"generated\sdat\atlas{atlas}_{kind}.xbt";
                files[level + file] = FromTemplate(srcLevel + file);
            }
        }

        FcbObject mapsData = FcbDocument.Deserialize(FromTemplate($@"{srcWorld}generated\tmpla.mapsdata.fcb"));
        RenameGroup(mapsData, name);
        MoveSpawnPoint(mapsData, spawn ?? OnTheGround(name, side, files));
        files[$@"{world}generated\{name}.mapsdata.fcb"] = FcbDocument.Serialize(mapsData);

        return files;
    }

    /// <summary>The template groups everything per map under a node named after it, found by that name's hash.</summary>
    private static void RenameGroup(FcbObject root, string name)
    {
        FcbObject group = root.Children.SingleOrDefault(c => c.TypeHash == TemplateGroup)
            ?? throw new InvalidDataException($"No group named {Template} to rename.");
        group.TypeHash = FcbClassDefinitions.Crc32Ascii(name);
    }

    private static Vector3 OnTheGround(string name, int side, IReadOnlyDictionary<string, byte[]> files)
    {
        TerrainMap map = TerrainMap.Discover(files.Keys).Single(m => m.Name == name);
        WorldTerrain terrain = WorldTerrain.Load(map, path => files.GetValueOrDefault(path));
        float middle = side * NavMeshGenerator.SectorSize / 2;
        return new Vector3(middle, middle, terrain.HeightMetersAt(middle, middle) + SpawnAboveGround);
    }

    private static void MoveSpawnPoint(FcbObject mapsData, Vector3 at)
    {
        FcbObject point = FcbFragments.LayersOf(mapsData).SelectMany(l => l.Children)
            .SingleOrDefault(e => FcbEntityFields.ReadString(e, WorldHashes.HidName) == "SpawnPoint_0")
            ?? throw new InvalidDataException($"{Template}'s mapsdata has no SpawnPoint_0.");
        point.Values[WorldHashes.HidPos] = FcbEntityFields.Vector3Bytes(at);
        point.Values[WorldHashes.HidPosPrecise] = FcbEntityFields.Vector3Bytes(at);
    }

    /// <summary>A sector's descriptor lists the other sectors; a smaller world keeps only its own.</summary>
    private static byte[] CutDescriptor(byte[] bytes, IReadOnlySet<int> sectors)
    {
        FcbObject root = FcbDocument.Deserialize(bytes);
        foreach (FcbObject list in root.Children.Where(c => c.TypeHash == SectorDesc))
        {
            list.Children.RemoveAll(s => s.TypeHash == DescSector
                && !(FcbEntityFields.ReadU32(s, DescSectorId) is uint id && sectors.Contains((int)id)));
        }
        return FcbDocument.Serialize(root);
    }

    /// <summary>
    /// The level navmesh index is a grid and extent declaration followed by an all-zero per-sector
    /// table and 24 trailing bytes. See docs/docs/file-formats/nvm.md#level-file-header-measured.
    /// </summary>
    private static byte[] NavMeshIndex(byte[] template, int side)
    {
        byte[] index = [.. template.AsSpan(0, 0x50), .. new byte[4 * side * side], .. template.AsSpan(template.Length - 24)];
        foreach (int at in new[] { 0x18, 0x1C, 0x34, 0x38 })
        {
            BitConverter.TryWriteBytes(index.AsSpan(at), side * NavMeshGenerator.SectorSize);
        }
        BitConverter.TryWriteBytes(index.AsSpan(0x40), side);
        BitConverter.TryWriteBytes(index.AsSpan(0x44), side);
        BitConverter.TryWriteBytes(index.AsSpan(0x4C), side * side);
        return index;
    }
}
