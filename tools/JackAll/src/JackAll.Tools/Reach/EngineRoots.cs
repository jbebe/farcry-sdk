using System.Text.RegularExpressions;

namespace JackAll.Tools.Reach;

public enum RootKind
{
    Literal,
    Pattern,
    World,
    Family,
    Fallback,
}

/// <summary>One line of the roots asset.</summary>
public sealed record RootRule(
    RootKind Kind,
    ReachFlags Flags,
    /// <summary>True when the flags come from the pattern's <c>(?&lt;world&gt;…)</c> capture via the
    /// world rules instead of the flags column.</summary>
    bool FlagsFromWorld,
    string Value,
    string? Label,
    int LineNumber)
{
    /// <summary>Compiled form of a pattern/world/fallback value; null for literals and families.</summary>
    public Regex? Regex { get; init; }
}

/// <summary>What the roots say about one corpus path.</summary>
public sealed class RootMatch
{
    public ReachFlags Flags;
    public string? Reason;
    /// <summary>Set when a WORLD pattern matched but the world rule says NONE (e.g. tmpla).</summary>
    public string? SuppressedReason;
    /// <summary>Set when a fallback rule matched - the engine knows this name but only reads it
    /// when its primary is absent.</summary>
    public string? FallbackReason;
    /// <summary>World tokens no world rule classifies - curation gaps to warn about.</summary>
    public List<string>? UnknownWorldTokens;
}

/// <summary>
/// The engine's reachability roots: hardcoded paths and name-composition patterns extracted from
/// Dunia.dll, curated into <c>assets/engine-roots.tsv</c>.
/// </summary>
/// <remarks>
/// Patterns are matched against the shipped corpus rather than printf-expanded - a root only
/// matters if the file shipped, and matching sidesteps re-deriving every format string's iteration
/// domain. An instance tracks per-rule match counts across one sweep, so it is single-run:
/// <see cref="UnmatchedRules"/> is only meaningful after every corpus path went through
/// <see cref="Match"/>.
/// </remarks>
public sealed class EngineRoots
{
    private readonly Dictionary<string, RootRule> _literals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RootRule> _patterns = [];
    private readonly List<RootRule> _worldRules = [];
    private readonly List<RootRule> _sourceFamilies = [];
    private readonly List<RootRule> _prefixFamilies = [];
    private readonly List<RootRule> _fallbacks = [];
    private readonly Dictionary<RootRule, int> _matchCounts = [];

    public IReadOnlyList<RootRule> Rules { get; }

    private EngineRoots(List<RootRule> rules)
    {
        Rules = rules;
        foreach (RootRule rule in rules)
        {
            switch (rule.Kind)
            {
                case RootKind.Literal:
                    _literals[rule.Value] = rule;
                    break;
                case RootKind.Pattern:
                    _patterns.Add(rule);
                    break;
                case RootKind.World:
                    _worldRules.Add(rule);
                    break;
                case RootKind.Family when rule.Value.StartsWith("source:", StringComparison.OrdinalIgnoreCase):
                    _sourceFamilies.Add(rule with { Value = rule.Value["source:".Length..] });
                    break;
                case RootKind.Family:
                    _prefixFamilies.Add(rule with { Value = rule.Value["prefix:".Length..] });
                    break;
                case RootKind.Fallback:
                    _fallbacks.Add(rule);
                    break;
            }
        }
    }

    public static EngineRoots Load(string path) => Parse(File.ReadLines(path));

    public static EngineRoots Parse(IEnumerable<string> lines)
    {
        var rules = new List<RootRule>();
        int lineNumber = 0;
        foreach (string raw in lines)
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length is < 3 or > 4)
            {
                throw new InvalidDataException($"engine-roots line {lineNumber}: expected 3-4 tab-separated fields, got {fields.Length}.");
            }

            RootKind kind = fields[0].ToLowerInvariant() switch
            {
                "literal" => RootKind.Literal,
                "pattern" => RootKind.Pattern,
                "world" => RootKind.World,
                "family" => RootKind.Family,
                "fallback" => RootKind.Fallback,
                _ => throw new InvalidDataException($"engine-roots line {lineNumber}: unknown kind '{fields[0]}'."),
            };

            ReachFlags flags = ParseFlags(fields[1], lineNumber, out bool fromWorld);
            if (fromWorld && kind != RootKind.Pattern)
            {
                throw new InvalidDataException($"engine-roots line {lineNumber}: WORLD flags are only valid on a pattern.");
            }

            string value = fields[2];
            if (kind == RootKind.Family
                && !value.StartsWith("source:", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("prefix:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"engine-roots line {lineNumber}: family value must start with source: or prefix:.");
            }

            var rule = new RootRule(kind, flags, fromWorld, value, fields.Length == 4 ? fields[3] : null, lineNumber);
            if (kind is RootKind.Pattern or RootKind.World or RootKind.Fallback)
            {
                rule = rule with
                {
                    Regex = new Regex(
                        kind == RootKind.World ? $"^(?:{value})$" : value,
                        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant),
                };
            }
            rules.Add(rule);
        }
        return new EngineRoots(rules);
    }

    /// <summary>Everything the roots say about one corpus file. Counts matches per rule.</summary>
    public RootMatch Match(string path, string sourceName)
    {
        var result = new RootMatch();

        if (_literals.TryGetValue(path, out RootRule? literal))
        {
            Apply(result, literal, "root:literal");
        }

        foreach (RootRule family in _sourceFamilies)
        {
            if (string.Equals(sourceName, family.Value, StringComparison.OrdinalIgnoreCase))
            {
                Apply(result, family, $"root:family(source:{family.Value})");
            }
        }
        foreach (RootRule family in _prefixFamilies)
        {
            if (path.StartsWith(family.Value, StringComparison.OrdinalIgnoreCase))
            {
                Apply(result, family, $"root:family(prefix:{family.Value})");
            }
        }

        foreach (RootRule pattern in _patterns)
        {
            var m = pattern.Regex!.Match(path);
            if (!m.Success)
            {
                continue;
            }

            if (!pattern.FlagsFromWorld)
            {
                Apply(result, pattern, $"root:pattern#{pattern.LineNumber}");
                continue;
            }

            string token = m.Groups["world"].Value;
            RootRule? worldRule = _worldRules.FirstOrDefault(w => w.Regex!.IsMatch(token));
            if (worldRule is null)
            {
                (result.UnknownWorldTokens ??= []).Add(token);
                continue;
            }

            Count(worldRule);
            if (worldRule.Flags == ReachFlags.None)
            {
                Count(pattern);
                result.SuppressedReason ??= worldRule.Label ?? $"dev-leftover({token})";
            }
            else
            {
                Apply(result, pattern, $"root:pattern#{pattern.LineNumber}({token})", worldRule.Flags);
            }
        }

        foreach (RootRule fallback in _fallbacks)
        {
            if (fallback.Regex!.IsMatch(path))
            {
                Count(fallback);
                result.FallbackReason ??= fallback.Label ?? "fallback:primary-present";
            }
        }

        return result;
    }

    /// <summary>Rules that matched nothing across the sweep - a typo'd pattern or a literal the
    /// install doesn't ship, which would otherwise fail silently.</summary>
    public IEnumerable<RootRule> UnmatchedRules()
        => Rules.Where(r => r.Kind != RootKind.World && _matchCounts.GetValueOrDefault(r) == 0);

    private void Apply(RootMatch result, RootRule rule, string reason, ReachFlags? flags = null)
    {
        Count(rule);
        result.Flags |= flags ?? rule.Flags;
        result.Reason ??= reason;
    }

    private void Count(RootRule rule) => _matchCounts[rule] = _matchCounts.GetValueOrDefault(rule) + 1;

    private static ReachFlags ParseFlags(string text, int lineNumber, out bool fromWorld)
    {
        fromWorld = false;
        switch (text.ToUpperInvariant())
        {
            case "GLOBAL":
                return ReachFlagsExtensions.Global;
            case "WORLD":
                fromWorld = true;
                return ReachFlags.None;
            case "NONE":
                return ReachFlags.None;
        }

        ReachFlags flags = ReachFlags.None;
        foreach (string part in text.Split('|'))
        {
            flags |= part.ToUpperInvariant() switch
            {
                "SP" => ReachFlags.SP,
                "MP" => ReachFlags.MP,
                "EDITOR" => ReachFlags.Editor,
                _ => throw new InvalidDataException($"engine-roots line {lineNumber}: unknown flag '{part}'."),
            };
        }
        return flags;
    }
}
