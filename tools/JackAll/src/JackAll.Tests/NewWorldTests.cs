using System.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// A new world made from tmpla. The fixtures are tmpla's own copies of the files New map edits or
/// reads; every file it only copies reads as a stand-in.
/// </summary>
public class NewWorldTests
{
    private const string Descriptor = "NewWorld/tmpla.game.xml";
    private const string MapsData = "NewWorld/tmpla.mapsdata.fcb";
    private const string SectorsDepFile = "NewWorld/tmpla.sectorsdep.fcb";

    private static readonly Dictionary<string, string> Fixtures = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"worlds\tmpla\generated\tmpla.game.xml"] = Descriptor,
        [@"worlds\tmpla\generated\tmpla.mapsdata.fcb"] = MapsData,
        [@"worlds\tmpla\generated\tmpla.sectorsdep.fcb"] = SectorsDepFile,
        [@"worlds\tmpla\generated\nv\sectors\nv.nvm"] = "NewWorld/nv.nvm",
        [@"levels\tmpla\generated\worldsectors\sector0.desc.fcb"] = "NewWorld/sector0.desc.fcb",
        [@"levels\tmpla\generated\sdat\sd0.sdat"] = "NewWorld/sd0.sdat",
    };

    private static byte[]? Read(string path) => Fixtures.TryGetValue(path, out string? fixture) ? Fixture.Read(fixture) : [];

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent([.. Fixtures.Values]);

    [Fact]
    public void A_one_sector_world_declares_one_sector_under_its_own_name()
    {
        if (!Fixture.Present(Descriptor)) return;

        SortedDictionary<string, byte[]> files = NewWorld.Create("range", 1, Read);

        string descriptor = Encoding.UTF8.GetString(files[@"worlds\range\generated\range.game.xml"]);
        Assert.Contains(@"Name=""Levels\range\range.nomad""", descriptor);
        Assert.DoesNotContain(@"Levels\tmpla", descriptor);
        Assert.DoesNotContain(@"CountX=""8""", descriptor);

        FcbObject sectorsDep = FcbDocument.Deserialize(files[@"worlds\range\generated\range.sectorsdep.fcb"]);
        FcbObject group = Assert.Single(sectorsDep.Children);
        Assert.Equal(FcbClassDefinitions.Crc32Ascii("range"), group.TypeHash);
        Assert.Single(group.Children);

        byte[] index = files[@"worlds\range\generated\nv\sectors\nv.nvm"];
        Assert.Equal(64f, BitConverter.ToSingle(index, 0x18));
        Assert.Equal(1, BitConverter.ToInt32(index, 0x4C));
        Assert.Equal(0x50 + 4 + 24, index.Length);

        Assert.DoesNotContain(files.Keys, p => p.EndsWith("entitylibrary.fcb", StringComparison.OrdinalIgnoreCase)
            || p.Contains(@"\worldsectors\worldsector", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_spawn_point_stands_on_the_ground_in_the_middle()
    {
        if (!Fixture.Present(MapsData)) return;

        FcbObject mapsData = FcbDocument.Deserialize(NewWorld.Create("range", 1, Read)[@"worlds\range\generated\range.mapsdata.fcb"]);
        FcbObject spawn = Entities(mapsData).Single(e => FcbEntityFields.ReadString(e, WorldHashes.HidName) == "SpawnPoint_0");

        // tmpla's plain is flat at 16.0625 m.
        Assert.Equal(new System.Numerics.Vector3(32, 32, 16.0625f + 0.44f), FcbEntityFields.ReadVector3(spawn, WorldHashes.HidPos));
        Assert.Equal(spawn.Values[WorldHashes.HidPos], spawn.Values[WorldHashes.HidPosPrecise]);
    }

    /// <summary>A new world's sector has no file until it is packed; the editor still places into it,
    /// and saves the placement as a fragment of the file the sector will have.</summary>
    [Fact]
    public void An_entity_placed_in_a_new_worlds_empty_sector_saves_as_its_fragment()
    {
        var map = new TerrainMap { Name = "range", SectorsPerSide = 1, Sectors = [(@"levels\range\generated\sdat\sd0.sdat", 0)] };
        Fc2World world = WorldLoader.Load(map, _ => null, everySector: true);
        var session = new WorldEditSession(world, map.SectorsPerSide);
        var wall = new ArchetypeDefinition(
            "OA_CoverObjects.CoverObjects.WallBarrier01_BK.Multi", new ArchetypeLayer("library.fcb"), 0, null,
            new FcbObject { TypeHash = WorldHashes.Entity });

        session.Place(wall, new System.Numerics.Vector3(20, 30, 16), MissionLayers.MainName);

        EntityFragment fragment = Assert.Single(session.Pending().Fragments);
        Assert.Equal(@"levels\range\generated\worldsectors\worldsector0.data.fcb", fragment.ContainerPath);
        Assert.Empty(session.LayerPlacements());
    }

    [Theory]
    [InlineData("Range")]
    [InlineData("1range")]
    [InlineData("tmpla")]
    [InlineData("my range")]
    public void A_name_the_engine_cannot_take_is_refused(string name)
        => Assert.Throws<ArgumentException>(() => NewWorld.Create(name, 1, Read));

    private static IEnumerable<FcbObject> Entities(FcbObject node)
        => node.Children.SelectMany(c => c.TypeHash == WorldHashes.Entity ? [c] : Entities(c));
}
