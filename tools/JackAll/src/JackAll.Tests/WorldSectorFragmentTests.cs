using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Deep fragments over a real <c>worldsector*.data.fcb</c> (docs/design/fcb-deep-fragments.md): one
/// override unit per placed entity, keyed on its <c>disEntityId</c>, so editing a single entity no
/// longer stages â€” or conflicts over â€” the whole sector. The fixture is a median-sized sector
/// extracted from the dedicated server's worlds.fat (12 entities over 2 mission layers).
/// </summary>
[Trait("Category", "RequiresFixture")]
public class WorldSectorFragmentTests : IDisposable
{
    private const string FixturePath = "Fixtures/WorldSector/worldsector56.data.fcb";

    /// <summary>The fixture's real in-archive path, so staged override paths hash to the same
    /// container hash the VFS would use.</summary>
    private const string SectorPath = @"levels\mp_14_woodlands\generated\worldsectors\worldsector56.data.fcb";

    private readonly string _sandbox =
        Path.Combine(Path.GetTempPath(), "jackall-worldsector-fragment", Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_fixture_file_was_actually_found()
        => Assert.True(File.Exists(FixturePath),
            $"{FixturePath} was not found - every test in this class silently no-opped.");

    [Fact]
    public void Every_entity_with_a_disEntityId_gets_one_uniquely_addressable_fragment()
    {
        if (!File.Exists(FixturePath)) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(FixturePath));
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(root);

        int addressableEntities = root.Children
            .Where(layer => layer.TypeHash == WorldHashes.MissionLayer)
            .SelectMany(layer => layer.Children)
            .Count(e => e.TypeHash == WorldHashes.Entity
                        && e.Values.TryGetValue(WorldHashes.DisEntityId, out byte[]? id) && id.Length >= 8);

        Assert.True(addressableEntities > 0, "The fixture has no addressable entities - it proves nothing.");
        Assert.Equal(addressableEntities, fragments.Count);
        Assert.Equal(fragments.Count, fragments.Select(f => f.Id).Distinct(FcbFragments.IdComparer).Count());
        Assert.All(fragments, f =>
        {
            Assert.Matches(@"^[^\\]+\.\d+\.xml$", f.Id);
            ulong disEntityId = BitConverter.ToUInt64(f.Node.Values[WorldHashes.DisEntityId], 0);
            // The trailing numeric id is authoritative: the canonical form is the bare id alone.
            Assert.Equal($"{disEntityId}.xml", FcbFragments.Canonicalize(f.Id));
        });
    }

    [Fact]
    public void Replacing_one_entity_changes_only_that_entity()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(original);

        string targetId = fragments[0].Id;
        byte[] editedXml = TestSupport.RenderWithValueSetAt(fragments[0].Node, [], 0xAAAA0001, [0x2A, 0x00, 0x00, 0x00]);

        byte[] assembled = FcbAssembler.Apply(
            baseFcb, new Dictionary<string, string> { [targetId] = System.Text.Encoding.UTF8.GetString(editedXml) });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        IReadOnlyList<FcbFragment> rebuiltFragments = FcbFragments.List(rebuilt);
        Assert.Equal(fragments.Count, rebuiltFragments.Count);

        for (int i = 0; i < fragments.Count; i++)
        {
            Assert.Equal(fragments[i].Id, rebuiltFragments[i].Id);
            string expected = FcbFragments.IdComparer.Equals(fragments[i].Id, targetId)
                ? System.Text.Encoding.UTF8.GetString(editedXml)
                : FcbXml.ToXml(fragments[i].Node, FcbClassDefinitions.Empty);
            Assert.Equal(expected, FcbXml.ToXml(rebuiltFragments[i].Node, FcbClassDefinitions.Empty));
        }
    }

    /// <summary>The name prefix is cosmetic: an override staged under a stale (renamed) name still
    /// finds and replaces its entity, because the trailing numeric id is what's matched.</summary>
    [Fact]
    public void An_override_staged_under_a_renamed_entity_still_resolves_by_its_numeric_id()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        FcbFragment target = FcbFragments.List(original)[0];
        ulong disEntityId = BitConverter.ToUInt64(target.Node.Values[WorldHashes.DisEntityId], 0);

        Assert.Same(target.Node, FcbFragments.Find(original, $"some_other_name.{disEntityId}.xml"));
        Assert.Same(target.Node, FcbFragments.Find(original, $"{disEntityId}.xml"));

        byte[] editedXml = TestSupport.RenderWithValueSetAt(target.Node, [], 0xAAAA0001, [0x01, 0x00, 0x00, 0x00]);
        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [$"some_other_name.{disEntityId}.xml"] = System.Text.Encoding.UTF8.GetString(editedXml),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        FcbObject replaced = FcbFragments.Find(rebuilt, target.Id)!;
        Assert.Equal([0x01, 0x00, 0x00, 0x00], replaced.Values[0xAAAA0001]);
        Assert.Equal(FcbFragments.List(original).Count, FcbFragments.List(rebuilt).Count); // replaced, not appended
    }

    [Fact]
    public void A_new_entity_id_is_appended_into_a_mission_layer_not_at_the_root()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        var addition = new FcbObject { TypeHash = WorldHashes.Entity };
        addition.Values.Add(WorldHashes.DisEntityId, BitConverter.GetBytes(999999999UL));
        string additionXml = FcbXml.ToXml(addition, FcbClassDefinitions.Empty);

        byte[] assembled = FcbAssembler.Apply(
            baseFcb, new Dictionary<string, string> { ["brand_new.999999999.xml"] = additionXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        // The fixture's second layer is its "main" one, so the append must land there - not in the
        // first layer, and not at the root.
        FcbObject mainLayer = rebuilt.Children.First(c =>
            c.TypeHash == WorldHashes.MissionLayer
            && FcbEntityFields.ReadString(c, WorldHashes.TextPathId).Equals("main", StringComparison.OrdinalIgnoreCase));
        FcbObject added = mainLayer.Children[^1];
        Assert.Equal(BitConverter.GetBytes(999999999UL), added.Values[WorldHashes.DisEntityId]);
        Assert.Contains(FcbFragments.List(rebuilt), f => FcbFragments.IdComparer.Equals(f.Id, "999999999.xml"));
    }

    /// <summary>The point of the whole feature: two mods editing *different* entities of the same
    /// sector merge instead of conflicting at file level.</summary>
    [Fact]
    public void Two_layers_overriding_different_entities_of_one_sector_both_survive_a_build()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject vanillaRoot = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(vanillaRoot);
        Assert.True(fragments.Count >= 2);

        FolderModLayer modA = MakeLayer("mod_a", fragments[0].Id,
            TestSupport.RenderWithValueSetAt(fragments[0].Node, [], 0xAAAA0001, [0x01, 0x00, 0x00, 0x00]));
        FolderModLayer modB = MakeLayer("mod_b", fragments[1].Id,
            TestSupport.RenderWithValueSetAt(fragments[1].Node, [], 0xAAAA0002, [0x02, 0x00, 0x00, 0x00]));

        var overrides = FragmentMerge.BuildOverrideIndex([modA, modB]);
        var byFragment = overrides[NameHash.Compute(SectorPath)];
        Assert.Equal(2, byFragment.Count);

        Dictionary<string, string> xmlById = byFragment.ToDictionary(
            kv => kv.Key,
            kv => FragmentMerge.Resolve(vanillaRoot, kv.Key, kv.Value, FcbClassDefinitions.Empty));
        FcbObject rebuilt = FcbDocument.Deserialize(FcbAssembler.Apply(baseFcb, xmlById));

        Assert.Equal(
            [0x01, 0x00, 0x00, 0x00],
            FcbFragments.Find(rebuilt, fragments[0].Id)!.Values[0xAAAA0001]);
        Assert.Equal(
            [0x02, 0x00, 0x00, 0x00],
            FcbFragments.Find(rebuilt, fragments[1].Id)!.Values[0xAAAA0002]);
    }

    /// <summary>A nested staged path (container path + fragment id) classifies back into the same
    /// container hash and an id the canonical comparer matches - the on-disk round trip.</summary>
    [Fact]
    public void A_staged_entity_override_round_trips_through_the_mod_layer_scan()
    {
        if (!File.Exists(FixturePath)) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(FixturePath));
        FcbFragment fragment = FcbFragments.List(root)[0];

        FolderModLayer layer = MakeLayer("roundtrip", fragment.Id, "<object hash=\"0984415E\" />"u8.ToArray());
        layer.Rescan();

        Assert.True(layer.FragmentOverrides.TryGetValue(NameHash.Compute(SectorPath), out var staged));
        FragmentOverride single = Assert.Single(staged!);
        Assert.True(FcbFragments.IdComparer.Equals(fragment.Id, single.FragmentId));
    }

    private FolderModLayer MakeLayer(string name, string fragmentId, byte[] content)
    {
        string dir = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(dir);
        var layer = new FolderModLayer(dir, name);
        layer.Stage(
            NameHash.Compute($"{SectorPath}\\{fragmentId}"), $"{SectorPath}\\{fragmentId}", "xml", content);
        return layer;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        }
        catch { /* temp cleanup is best-effort */ }
    }
}

