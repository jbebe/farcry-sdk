using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Ai;
using JackAll.Tools.World;

namespace JackAll.Tests;

public class AiTuningTests
{
    private const string GameModes = "Ai/gamemodesconfig.xml";
    private const string Assault = "enemy_archetypes.Red_Faction.Assault_Caucasian";

    private static readonly Lazy<ArchetypeIndex?> World1 = new(() => Fixture.Present(FcbDocumentTests.World1)
        ? ArchetypeIndex.Load([new ArchetypeLayer(@"worlds\world1\generated\entitylibrary.fcb")], _ => Fixture.Read(FcbDocumentTests.World1))
        : null);

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(GameModes, FcbDocumentTests.World1);

    [Fact]
    public void Every_soldier_field_resolves_on_a_shipped_enemy()
    {
        if (World1.Value?.Winner(Assault)?.Node is not { } entity) return;

        Assert.True(SoldierFields.Catalog.Covers(entity));
        Assert.Empty(SoldierFields.All.Where(f => f.Read(entity) is null).Select(f => f.Label));
    }

    [Fact]
    public void Every_weapon_field_resolves_and_the_ak_lets_hits_chain_on_infamous()
    {
        if (World1.Value?.Winner("WeaponProperties.Primary.AK47")?.Node is not { } ak) return;

        Assert.True(WeaponFields.Catalog.Covers(ak));
        Assert.False(WeaponFields.Catalog.Covers(World1.Value!.Winner(Assault)!.Node));
        Assert.Empty(WeaponFields.All.Where(f => f.Read(ak) is null).Select(f => f.Label));
        Assert.Equal([4.0, 8, 2, 4, 1, 1, 0, 0], WeaponFields.All.Take(8).Select(f => f.Read(ak)!.Value));
    }

    [Fact]
    public void The_unnamed_vision_cone_members_are_length_and_angle()
    {
        if (World1.Value?.Winner(Assault)?.Node is not { } entity) return;

        Assert.Equal(0xC2199C86u, FcbClassDefinitions.Crc32Ascii("fLength"));
        Assert.Equal(0x680DD0F4u, FcbClassDefinitions.Crc32Ascii("fAngle"));
        Assert.Equal(60, Field("Desert: focus range (m)").Read(entity));
        Assert.Equal(120, Field("Desert: side angle (°)").Read(entity));
    }

    [Fact]
    public void Writing_a_field_changes_only_that_member()
    {
        if (World1.Value?.Winner(Assault)?.Node is not { } shipped) return;

        FcbObject entity = shipped.Clone();
        TuningField reaction = Field("Reaction time (s)");
        Assert.True(reaction.Write(entity, 0.9));

        Assert.Equal(0.9, reaction.Read(entity)!.Value, 5);
        Assert.DoesNotContain(SoldierFields.All, f => f != reaction && f.Read(entity) != f.Read(shipped));
    }

    [Fact]
    public void A_soldier_edited_back_to_its_base_value_plans_a_vanilla_fragment()
    {
        if (Fixture.Read(FcbDocumentTests.World1) is not { } library || World1.Value?.Winner(Assault) is not { } assault) return;

        TuningCopy copy = Assert.Single(TuningLibrary.Open(SoldierFields.Catalog, [assault], library, library));
        FcbClassDefinitions definitions = BundledAssets.LoadFcbClasses();
        TuningField reaction = Field("Reaction time (s)");
        double shipped = reaction.Read(copy.Entity)!.Value;

        reaction.Write(copy.Entity, shipped + 1);
        Assert.True(copy.IsEdited);
        Assert.False(copy.Plan(definitions).IsVanilla);

        reaction.Write(copy.Entity, shipped);
        Assert.False(copy.IsEdited);
        Assert.True(copy.Plan(definitions).IsVanilla);
    }

    [Fact]
    public void Rewriting_the_behaviour_table_unchanged_leaves_the_file_alone()
    {
        if (Fixture.ReadText(GameModes) is not { } xml) return;

        IReadOnlyList<AdaptiveBehavior> rows = AdaptiveBehaviors.Read(xml);

        Assert.Equal(12, rows.Count);
        Assert.Equal(20, rows.Single(r => r.Name == "Grenade").Chances[27]);
        Assert.Equal(xml, AdaptiveBehaviors.Write(xml, rows));
    }

    [Fact]
    public void An_edited_chance_lands_in_its_own_level_only()
    {
        if (Fixture.ReadText(GameModes) is not { } xml) return;

        List<AdaptiveBehavior> rows = [.. AdaptiveBehaviors.Read(xml)];
        rows.Single(r => r.Name == "Grenade").Chances[3] = 42.5;

        IReadOnlyList<AdaptiveBehavior> reread = AdaptiveBehaviors.Read(AdaptiveBehaviors.Write(xml, rows));

        AdaptiveBehavior grenade = reread.Single(r => r.Name == "Grenade");
        Assert.Equal(42.5, grenade.Chances[3]);
        Assert.Equal(5, grenade.Chances[4]);
        Assert.Equal(10, reread.Single(r => r.Name == "GrenadeAndBuilding").Chances[0]);
    }

    private static TuningField Field(string label) => SoldierFields.All.Single(f => f.Label == label);
}
