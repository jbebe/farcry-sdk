namespace JackAll.Core.Format.Move.Rules;

/// <summary>Three-valued: a test on a channel the situation leaves open is <see cref="Maybe"/>.</summary>
public enum MoveMatch
{
    No,
    Maybe,
    Yes,
}

/// <summary>What one rule does in a situation, given the rules the engine tries before it.</summary>
public enum MoveVerdict
{
    /// <summary>The engine picks this rule.</summary>
    Plays,

    /// <summary>Picked unless an earlier rule that depends on an open channel is picked first.</summary>
    MayPlay,

    /// <summary>Its own tests pass, but an earlier rule always wins.</summary>
    Shadowed,

    No,
}

/// <summary>What is known about the character right now: a value for some of the channels.</summary>
public sealed class MoveSituation
{
    private readonly Dictionary<int, double> _values = [];

    public double? this[int channel]
    {
        get => _values.TryGetValue(channel, out double value) ? value : null;
        set
        {
            if (value is { } v)
            {
                _values[channel] = v;
            }
            else
            {
                _values.Remove(channel);
            }
        }
    }

    public MoveMatch Test(MoveCondition condition)
    {
        if (this[condition.Channel] is not { } value)
        {
            return MoveMatch.Maybe;
        }

        return condition.Test(value) switch
        {
            true => MoveMatch.Yes,
            false => MoveMatch.No,
            null => MoveMatch.Maybe,
        };
    }

    /// <summary>A criteria list: each operator joins its criterion to everything before it, left to right.</summary>
    public MoveMatch Test(IReadOnlyList<MoveCondition> list)
    {
        if (list.Count == 0)
        {
            return MoveMatch.Yes;
        }

        MoveMatch result = Test(list[0]);
        for (int i = 1; i < list.Count; i++)
        {
            MoveMatch next = Test(list[i]);
            result = list[i].IsOr ? Max(result, next) : Min(result, next);
        }

        return result;
    }

    /// <summary>Every node on the way down has to pass.</summary>
    public MoveMatch Test(MoveRule rule)
    {
        if (rule.IsDisabled)
        {
            return MoveMatch.No;
        }

        MoveMatch result = MoveMatch.Yes;
        foreach (MoveObject node in rule.Chain)
        {
            result = Min(result, Test(MoveCondition.Of(node)));
            if (result == MoveMatch.No)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// What each of a state's rules does here, taken in search order; each transition target is a
    /// search of its own. With <paramref name="set"/>, a "go to" rule is followed into its state.
    /// </summary>
    public IReadOnlyList<MoveVerdict> Resolve(IReadOnlyList<MoveRule> rules, MoveRuleSet? set = null)
    {
        List<MoveVerdict> verdicts = [];
        bool decided = false;
        bool contested = false;
        MoveObject? entry = null;
        foreach (MoveRule rule in rules)
        {
            if (rule.Entry != entry)
            {
                (entry, decided, contested) = (rule.Entry, false, false);
            }

            MoveMatch match = Test(rule, set, []);
            if (match == MoveMatch.No)
            {
                verdicts.Add(MoveVerdict.No);
            }
            else if (decided)
            {
                verdicts.Add(MoveVerdict.Shadowed);
            }
            else if (match == MoveMatch.Yes)
            {
                verdicts.Add(contested ? MoveVerdict.MayPlay : MoveVerdict.Plays);
                decided = true;
            }
            else
            {
                verdicts.Add(MoveVerdict.MayPlay);
                contested = true;
            }
        }

        return verdicts;
    }

    /// <summary>
    /// A rule's own tests, and for a "go to" rule whether the state it enters finds anything: when it
    /// does not, the engine backs out and carries on with the next rule.
    /// </summary>
    private MoveMatch Test(MoveRule rule, MoveRuleSet? set, HashSet<MoveObject> entered)
    {
        MoveMatch match = Test(rule);
        if (match == MoveMatch.No || set is null || rule.Target is not { } target)
        {
            return match;
        }

        if (!entered.Add(target))
        {
            return Min(match, MoveMatch.Maybe);
        }

        MoveMatch found = MoveMatch.No;
        foreach (MoveRule inner in set.RulesOf(target).Where(r => r.Entry is null))
        {
            found = Max(found, Test(inner, set, entered));
            if (found == MoveMatch.Yes)
            {
                break;
            }
        }

        entered.Remove(target);
        return Min(match, found);
    }

    private static MoveMatch Min(MoveMatch a, MoveMatch b) => a < b ? a : b;

    private static MoveMatch Max(MoveMatch a, MoveMatch b) => a > b ? a : b;
}
