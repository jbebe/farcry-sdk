using System.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>A new world's library cut from the full set, and the files packing builds for it.</summary>
public class WorldPackTests
{
    [Fact]
    public void The_cut_keeps_whole_groups_and_what_the_world_names_with_what_that_names()
    {
        FcbObject full = Library(
            ("weapons", "weapons.Primary.AK47", null),
            ("OA_Props", "OA_Props.Crate", "OA_Props.CrateLid"),
            ("OA_Props", "OA_Props.CrateLid", null),
            ("OA_Props", "OA_Props.Barrel", null));
        FcbObject placed = Named("OA_Props.Crate");

        string[] kept = [.. Names(LibraryCut.Cut(full, [placed]))];

        Assert.Equal(["weapons.Primary.AK47", "OA_Props.Crate", "OA_Props.CrateLid"], kept);
    }

    [Fact]
    public void A_packed_world_has_every_sector_flagged_and_its_library_cut()
    {
        Dictionary<string, byte[]> files = new(NewWorld.Create("range", 1, NewWorldTestsRead), StringComparer.OrdinalIgnoreCase)
        {
            [WorldPack.FullLibrary] = FcbDocument.Serialize(Library(("weapons", "weapons.Primary.AK47", null), ("OA_Props", "OA_Props.Barrel", null))),
        };
        if (!files.ContainsKey(@"levels\range\generated\sdat\sd0.sdat") || files[@"levels\range\generated\sdat\sd0.sdat"].Length == 0) return;
        TerrainMap map = TerrainMap.Discover(files.Keys).Single(m => m.Name == "range");

        SortedDictionary<string, byte[]> built = WorldPack.BuildWorld("range", map, files.GetValueOrDefault, FcbClassDefinitions.Empty);

        FcbObject sector = FcbDocument.Deserialize(built[@"levels\range\generated\worldsectors\worldsector0.data.fcb"]);
        Assert.Equal(0u, FcbEntityFields.ReadU32(sector, WorldHashes.WorldSectorX));
        Assert.Single(FcbFragments.LayersOf(sector));

        FcbObject entry = FcbDocument.Deserialize(built[@"worlds\range\generated\range.sectorsdep.fcb"]).Children.Single().Children.Single();
        Assert.Equal([1], entry.Values[FcbClassDefinitions.Crc32Ascii(SectorsDep.HasMainSectorData)]);
        Assert.Equal([1], entry.Values[FcbClassDefinitions.Crc32Ascii(SectorsDep.HasNavMesh)]);

        Assert.Equal(["weapons.Primary.AK47"], Names(FcbDocument.Deserialize(built[@"worlds\range\generated\entitylibrary.fcb"])));
        Assert.True(built.ContainsKey(@"levels\range\generated\nv\sectors\nv_0.nvm"));
    }

    private static byte[]? NewWorldTestsRead(string path)
        => path.EndsWith(@"tmpla.game.xml") ? Fixture.Read("NewWorld/tmpla.game.xml")
            : path.EndsWith(@"tmpla.mapsdata.fcb") ? Fixture.Read("NewWorld/tmpla.mapsdata.fcb")
            : path.EndsWith(@"tmpla.sectorsdep.fcb") ? Fixture.Read("NewWorld/tmpla.sectorsdep.fcb")
            : path.EndsWith(@"nv.nvm") ? Fixture.Read("NewWorld/nv.nvm")
            : path.EndsWith(@"sector0.desc.fcb") ? Fixture.Read("NewWorld/sector0.desc.fcb")
            : path.EndsWith(@"sd0.sdat") ? Fixture.Read("NewWorld/sd0.sdat") ?? []
            : [];

    /// <summary>A library root of groups, each prototype naming at most one other archetype.</summary>
    private static FcbObject Library(params (string Group, string Name, string? Names)[] prototypes)
    {
        var root = new FcbObject { TypeHash = WorldHashes.EntityLibraries };
        foreach (var group in prototypes.GroupBy(p => p.Group))
        {
            var node = new FcbObject { TypeHash = WorldHashes.EntityLibrary };
            node.Values[WorldHashes.Name] = Text(group.Key);
            foreach ((_, string name, string? names) in group)
            {
                FcbObject entity = names is null ? new FcbObject { TypeHash = WorldHashes.Entity } : Named(names);
                entity.TypeHash = WorldHashes.Entity;
                entity.Values[WorldHashes.HidName] = Text(name);
                var prototype = new FcbObject { TypeHash = WorldHashes.EntityPrototype };
                prototype.Children.Add(entity);
                node.Children.Add(prototype);
            }
            root.Children.Add(node);
        }
        return root;
    }

    private static FcbObject Named(string archetype)
    {
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        node.Values[WorldHashes.TplCreatureType] = Text(archetype);
        return node;
    }

    private static IEnumerable<string> Names(FcbObject library)
        => library.Children.SelectMany(g => g.Children)
            .Select(p => FcbEntityFields.ReadString(p.Children.Single(), WorldHashes.HidName));

    private static byte[] Text(string text) => [.. Encoding.UTF8.GetBytes(text), 0];
}
