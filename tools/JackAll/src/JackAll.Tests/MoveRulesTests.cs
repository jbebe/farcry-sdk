using JackAll.Core;
using JackAll.Core.Format.Move;
using JackAll.Core.Format.Move.Rules;

namespace JackAll.Tests;

/// <summary>
/// The rule view of a MOVE graph: every clip is reachable from a rule, rules read the way the
/// engine searches them, and a situation picks the rule <c>GetNextMovement</c> would.
/// </summary>
public sealed class MoveRulesTests
{
    private const int DartRifle = 39;

    /// <summary>The retail graph's bytes, or null when this checkout lacks it.</summary>
    internal static byte[]? RetailGraph => Retail.Value?.Graph;

    private static readonly Lazy<(byte[] Graph, IReadOnlyList<MoveChannel> Channels, MoveNames Names)?> Retail = new(() =>
        Fixture.Read(MoveStateIndexTests.Manager) is { } graph && Fixture.Read(MoveStateIndexTests.Named) is { } named
            ? (graph, MoveCodec.ChannelTable(named), BundledAssets.LoadMoveNames())
            : null);

    /// <summary>A fresh session over the retail graph, or null when this checkout lacks it.</summary>
    internal static MoveEditSession? OpenRetail()
        => Retail.Value is { } retail
            ? new MoveEditSession(retail.Graph, retail.Graph, retail.Names, retail.Channels)
            : null;

    internal static int ChannelNamed(MoveEditSession session, string name)
        => Enumerable.Range(0, session.Channels.Count).Single(i => session.Channels.NameOf(i) == name);

    internal static int ValueNamed(MoveEditSession session, int channel, string name)
        => session.Channels.ValuesOf(channel)!.ToList().IndexOf(name);

    internal static IReadOnlyList<MoveRule> Reload(MoveEditSession session)
        => session.Rules.RulesOf(session.Index.ByHash(MoveNames.HashOf("Pawn_Generic_Reload"))!);

    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Every_clip_in_the_graph_is_reachable_from_a_rule(string path)
    {
        if (Fixture.Read(path) is not { } graph) return;

        MoveEditSession session = new MoveEditSession(graph, graph, MoveNames.Empty, null);
        HashSet<MoveObject> reached =
        [
            .. session.Rules.States.SelectMany(session.Rules.RulesOf).SelectMany(r => r.Clips).Select(c => c.Owner),
        ];

        Assert.All(session.File.Objects.Where(o => o.Field("m_animNameHash") is not null), o => Assert.Contains(o, reached));
        Assert.Empty(session.Plan().Changes);
    }

    [Fact]
    public void A_weapons_rules_reach_every_clip_its_branches_play()
    {
        if (OpenRetail() is not { } session) return;
        HashSet<uint> reached =
        [
            .. session.Rules.States.SelectMany(session.Rules.RulesOf)
                .Where(r => r.Pin?.Weapon == DartRifle)
                .SelectMany(r => r.Clips)
                .Select(c => c.Hash),
        ];

        Assert.Subset(reached, MoveWeapons.ClipsByWeapon(session.File)[DartRifle].ToHashSet());
    }

    [Fact]
    public void Reload_reads_as_the_three_situations_the_graph_distinguishes()
    {
        if (OpenRetail() is not { } session) return;
        List<string> dartRifle =
        [
            .. Reload(session)
                .Where(r => r.Entry is null && r.Pin?.Weapon == DartRifle)
                .Select(r => string.Join(" · ", r.Chain
                    .Select(n => MoveCondition.Describe(MoveCondition.Of(n), session.Channels))
                    .Where(t => t.Length > 0))),
        ];

        Assert.Equal(
        [
            "CameraPlacement = FirstPerson · EquippedWeapon = Dart_Rifle",
            "CameraPlacement = ThirdPerson · Stance = Crouched or Vehicle ≠ None · EquippedWeapon = Dart_Rifle",
            "CameraPlacement = ThirdPerson · EquippedWeapon = Dart_Rifle",
        ], dartRifle);
    }

    [Fact]
    public void A_situation_picks_the_first_rule_whose_tests_pass()
    {
        if (OpenRetail() is not { } session) return;
        IReadOnlyList<MoveRule> rules = Reload(session);
        int camera = ChannelNamed(session, "CameraPlacement");
        int stance = ChannelNamed(session, "Stance");
        int vehicle = ChannelNamed(session, "Vehicle");
        MoveRule crouched = rules.Where(r => r.Entry is null && r.Pin?.Weapon == DartRifle).ElementAt(1);
        MoveRule standing = rules.Where(r => r.Entry is null && r.Pin?.Weapon == DartRifle).ElementAt(2);

        MoveSituation situation = new()
        {
            [camera] = ValueNamed(session, camera, "ThirdPerson"),
            [MoveWeapons.EquippedWeaponChannel] = DartRifle,
            [stance] = ValueNamed(session, stance, "Crouched"),
        };
        IReadOnlyList<MoveVerdict> verdicts = situation.Resolve(rules);
        Assert.Equal(MoveVerdict.Plays, verdicts[crouched.Order]);
        Assert.Equal(MoveVerdict.Shadowed, verdicts[standing.Order]);

        // Standing, with the vehicle channel still open: crouched might yet apply, so neither is certain.
        situation[stance] = ValueNamed(session, stance, "Standing");
        verdicts = situation.Resolve(rules);
        Assert.Equal(MoveVerdict.MayPlay, verdicts[crouched.Order]);
        Assert.Equal(MoveVerdict.MayPlay, verdicts[standing.Order]);

        situation[vehicle] = ValueNamed(session, vehicle, "None");
        verdicts = situation.Resolve(rules);
        Assert.Equal(MoveVerdict.No, verdicts[crouched.Order]);
        Assert.Equal(MoveVerdict.Plays, verdicts[standing.Order]);
    }

    /// <summary>
    /// Crouched, jammed, first person with a Makarov: the one rule for that case sits behind a "go to
    /// the jump state" rule, which only wins when the jump state finds something.
    /// </summary>
    [Fact]
    public void A_go_to_rule_only_wins_when_the_state_it_enters_does()
    {
        const int Makarov = 20;
        if (OpenRetail() is not { } session) return;
        MoveObject aim = session.Index.ByHash(MoveNames.HashOf("Pawn_Generic_Aim"))!;
        MoveObject aimFirst = session.Index.ByHash(MoveNames.HashOf("Pawn_Generic_Aim_First"))!;
        Assert.Contains(aimFirst, session.Rules.Reachable(aim));

        IReadOnlyList<MoveRule> rules = session.Rules.RulesOf(aimFirst);
        MoveRule crouchedJam = rules.Single(r => r.Pin?.Weapon == Makarov && r.Entry is null
            && string.Join(" ", r.Chain.Select(n => MoveCondition.Describe(MoveCondition.Of(n), session.Channels)))
                is var text && text.Contains("Stance = Crouched", StringComparison.Ordinal) && text.Contains("Jammed", StringComparison.Ordinal));
        int stance = ChannelNamed(session, "Stance");
        int aimStance = ChannelNamed(session, "AimStance");
        MoveSituation situation = new()
        {
            [MoveWeapons.EquippedWeaponChannel] = Makarov,
            [stance] = ValueNamed(session, stance, "Crouched"),
            [ChannelNamed(session, "Jammed")] = 1,
            [aimStance] = ValueNamed(session, aimStance, "Normal"),
        };

        IReadOnlyList<MoveVerdict> verdicts = situation.Resolve(rules, session.Rules);
        Assert.Equal(MoveRuleKind.EnterState, rules[0].Kind);
        Assert.NotEqual(MoveVerdict.Plays, verdicts[0]);
        Assert.Contains(verdicts[crouchedJam.Order], new[] { MoveVerdict.Plays, MoveVerdict.MayPlay });

        // The parent enters Aim_First through a "go to" rule; its transitions stay in Aim_First's own list.
        Assert.DoesNotContain(session.Rules.RulesOf(aim), r => r.Node == crouchedJam.Node);
    }

    [Fact]
    public void A_criteria_list_folds_left_to_right_with_no_precedence()
    {
        // (a or b) and c, which "a or (b and c)" would get wrong when only a holds.
        MoveObject owner = new("CMoveGroup");
        foreach ((int channel, int op) in new[] { (1, 0), (2, 1), (3, 0) })
        {
            MoveObject criterion = new("CMoveCriteriaEnumEqual");
            criterion.Ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_Value", 1));
            criterion.Ops.Add(MoveOp.Integer(MoveOpKind.U8, "m_eValueID", (uint)channel));
            criterion.Ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_logicOperator", (uint)op));
            owner.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNew, "CMoveCriteria", criterion));
        }

        IReadOnlyList<MoveCondition> list = MoveCondition.Of(owner);
        MoveSituation situation = new() { [1] = 1, [2] = 0, [3] = 0 };
        Assert.Equal(MoveMatch.No, situation.Test(list));

        situation[3] = 1;
        Assert.Equal(MoveMatch.Yes, situation.Test(list));

        situation[1] = null;
        Assert.Equal(MoveMatch.Maybe, situation.Test(list));
    }
}
