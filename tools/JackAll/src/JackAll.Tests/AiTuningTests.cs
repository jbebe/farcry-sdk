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

        Assert.True(SoldierFields.IsSoldier(entity));
        Assert.Empty(SoldierFields.All.Where(f => f.Read(entity) is null).Select(f => f.Key));
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
        SoldierField reaction = Field("Reaction time (s)");
        Assert.True(reaction.Write(entity, 0.9));

        Assert.Equal(0.9, reaction.Read(entity)!.Value, 5);
        Assert.DoesNotContain(SoldierFields.All, f => f != reaction && f.Read(entity) != f.Read(shipped));
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

    private static SoldierField Field(string label) => SoldierFields.All.Single(f => f.Label == label);
}
