using JackAll.Core.Format.Move;
using JackAll.Core.Format.Move.Rules;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// Editing a graph as rules and saving it as the mod fragments that carry exactly those edits.
/// </summary>
/// <remarks>
/// The strongest check available without a game launch is the shipped VSS Vintorez: redoing its
/// clip swaps as rule edits has to produce the very fragments the mod ships.
/// </remarks>
public sealed class MoveEditSessionTests
{
    private const int DartRifle = 39;

    private static string VssFragments =>
        Path.Combine(TestSupport.RepositoryRoot, "mods", "vss-vintorez", "layer", "mods",
            "graphics", "move", "movemgr.bin");

    [Fact]
    public void Redoing_the_vss_clip_swaps_stages_exactly_the_fragments_it_ships()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        byte[] vanilla = MoveRulesTests.RetailGraph!;
        Dictionary<string, string> shipped = Directory.EnumerateFiles(VssFragments, "*.xml")
            .ToDictionary(p => Path.GetFileName(p), File.ReadAllText);
        MoveFile vss = MoveCodec.Load(MoveContainerSplitter.Instance.Apply(vanilla, shipped));

        for (int i = 0; i < vss.Objects.Count; i++)
        {
            if (vss.Objects[i].Field("m_animNameHash") is { } clip && clip != session.File.Objects[i].Field("m_animNameHash"))
            {
                session.SetClip(session.File.Objects[i], clip);
            }
        }

        MoveSavePlan plan = session.Plan();
        Assert.Equal(shipped.Keys.Order(), plan.Changes.Select(c => c.Id).Order());
        Assert.All(plan.Changes, c => Assert.Equal(
            MoveContainerSplitter.Instance.Canonicalize(c.Id, shipped[c.Id]), c.Xml));
        Assert.Equal(MoveCodec.Save(vss), plan.Result);
    }

    [Fact]
    public void Duplicating_then_deleting_a_rule_gives_the_graph_back()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        byte[] vanilla = MoveRulesTests.RetailGraph!;
        MoveRule rule = MoveRulesTests.Reload(session).First(r => r.Pin?.Weapon == DartRifle);
        int before = MoveRulesTests.Reload(session).Count;

        MoveObject copy = session.Duplicate(rule);
        Assert.Equal(before + 1, MoveRulesTests.Reload(session).Count);
        Assert.Same(copy, MoveRulesTests.Reload(session)[rule.Order].Node);
        Assert.NotEmpty(session.Plan().Changes);

        session.Delete(MoveRulesTests.Reload(session)[rule.Order]);
        Assert.Equal(vanilla, MoveCodec.Save(session.File));
        Assert.Empty(session.Plan().Changes);
    }

    [Fact]
    public void Moving_a_rule_reorders_its_group_and_moving_it_back_restores_it()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        byte[] vanilla = MoveRulesTests.RetailGraph!;
        MoveRule second = session.Rules.States
            .SelectMany(session.Rules.RulesOf)
            .First(r => r.Order > 0 && session.Rules.RulesOf(r.State)[r.Order - 1].Parent == r.Parent);
        MoveObject node = second.Node;

        session.Move(second, -1);
        MoveRule moved = session.Rules.RulesOf(second.State).Single(r => r.Node == node);
        Assert.Equal(second.Order - 1, moved.Order);
        session.Plan();

        session.Move(moved, +1);
        Assert.Equal(vanilla, MoveCodec.Save(session.File));
    }

    [Fact]
    public void A_condition_can_be_added_changed_and_removed()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        byte[] vanilla = MoveRulesTests.RetailGraph!;
        MoveRule rule = MoveRulesTests.Reload(session).First(r => r.Pin?.Weapon == DartRifle);
        int jammed = MoveRulesTests.ChannelNamed(session, "Jammed");

        session.AddCondition(rule.Node, new MoveConditionSpec(jammed, MoveConditionOp.Is, 1));
        MoveCondition added = MoveCondition.Of(rule.Node).Single();
        Assert.Equal("Jammed", added.Describe(session.Channels));

        MoveSavePlan plan = session.Plan();
        Assert.Equal(["Pawn_Generic_Reload_ch17_w39.1920121392.xml"], plan.Changes.Select(c => c.Id));

        session.SetCondition(rule.Node, added, new MoveConditionSpec(jammed, MoveConditionOp.Is, 0));
        Assert.Equal("not Jammed", MoveCondition.Of(rule.Node).Single().Describe(session.Channels));

        int stance = MoveRulesTests.ChannelNamed(session, "Stance");
        session.SetCondition(rule.Node, MoveCondition.Of(rule.Node).Single(),
            new MoveConditionSpec(stance, MoveConditionOp.IsNot, 1));
        Assert.Equal("CMoveCriteriaEnumNotEqual", MoveCondition.Of(rule.Node).Single().Criterion.ClassName);
        session.Plan();

        session.RemoveCondition(rule.Node, MoveCondition.Of(rule.Node).Single());
        Assert.Equal(vanilla, MoveCodec.Save(session.File));
    }

    [Fact]
    public void A_weapon_pin_cannot_be_edited_away()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        MoveRule rule = MoveRulesTests.Reload(session).First(r => r.Pin?.Weapon == DartRifle);
        MoveCondition pin = MoveCondition.Of(rule.PinOwner!).Single(c => c.Criterion == MoveUnits.PinCriterionOf(rule.PinOwner!));

        Assert.Throws<MoveEditException>(() => session.RemoveCondition(rule.PinOwner!, pin));
        Assert.Throws<MoveEditException>(() =>
            session.SetCondition(rule.PinOwner!, pin, pin.Spec with { Value = 40 }));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Deleting_a_rule_something_else_refers_to_is_refused()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        byte[] vanilla = MoveRulesTests.RetailGraph!;
        HashSet<MoveObject> referenced =
        [
            .. session.File.Objects.SelectMany(o => o.Ops)
                .Where(op => op.Kind == MoveOpKind.PointerRef && op.Name == "m_ptr")
                .Select(op => op.Target!),
        ];
        MoveRule rule = session.Rules.States.SelectMany(session.Rules.RulesOf)
            .First(r => referenced.Contains(r.Node) && r.Parent.Ops[r.SlotIndex].Kind == MoveOpKind.PointerNew);

        Assert.Throws<MoveEditException>(() => session.Delete(rule));
        Assert.Equal(vanilla, MoveCodec.Save(session.File));
    }

    [Fact]
    public void A_timing_edit_survives_a_round_trip()
    {
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        MoveClipSite site = MoveRulesTests.Reload(session).First(r => r.Pin?.Weapon == DartRifle).Clips[0];

        session.SetFloat(site.Owner, "m_flBlendTime", 0.25f);

        MoveFile reloaded = MoveCodec.Load(session.Plan().Result);
        Assert.Equal(0.25f, reloaded.Objects[session.File.Objects.IndexOf(site.Owner)].FieldF32("m_flBlendTime"));
    }

    [Fact]
    public void Copying_a_weapons_set_gives_the_new_index_the_donors_clips()
    {
        const int NewWeapon = 44;
        if (MoveRulesTests.OpenRetail() is not { } session) return;
        IReadOnlySet<uint> donorClips = MoveWeapons.ClipsByWeapon(session.File)[DartRifle];
        int packages = (int)session.File.Objects.Single(o => o.ClassName == "CMoveMgr").Field("size")!.Value;

        MoveCloneResult result = session.CloneWeapon(DartRifle, NewWeapon, "vss_test");

        Assert.NotEmpty(result.States);
        IReadOnlyDictionary<int, IReadOnlySet<uint>> after = MoveWeapons.ClipsByWeapon(session.File);
        Assert.Equal(donorClips.Order(), after[NewWeapon].Order());
        Assert.Equal(donorClips.Order(), after[DartRifle].Order());
        Assert.Equal(packages + 1, (int)session.File.Objects.Single(o => o.ClassName == "CMoveMgr").Field("size")!.Value);

        MoveSavePlan plan = session.Plan();
        Assert.Contains(plan.Changes, c => c.Id == "_packages.xml");
        Assert.Contains(plan.Changes, c => c.Id.Contains("_w44.", StringComparison.Ordinal));
    }
}
