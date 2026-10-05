using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JackAll.Core.Legacy;

/// <summary>
/// One feature page's frontmatter: a reusable component of a legacy mod, a bundle naming several,
/// or a noise rule claiming changes that do nothing in game.
/// </summary>
public sealed class LegacyFeature
{
    public static readonly string[] Kinds = ["component", "bundle", "noise"];
    public static readonly string[] Statuses = ["located", "partial", "no-artifact", "unresolved"];
    public static readonly string[] Verifications = ["diff", "re", "in-game"];

    /// <summary>The page's file name, which is how everything else refers to it.</summary>
    [YamlIgnore]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = "component";
    public string? Bundle { get; set; }
    public List<string> Claims { get; set; } = [];
    public string? Status { get; set; }
    public List<string> Systems { get; set; } = [];
    public List<string> Match { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
    public List<string> Requires { get; set; } = [];
    public List<string> Includes { get; set; } = [];
    public string? Existing { get; set; }
    public string? Verified { get; set; }

    [YamlIgnore]
    public bool ClaimsChanges => Kind != "bundle";

    private Regex[]? _match, _exclude;

    public bool Matches(string address)
    {
        _match ??= [.. Match.Select(AddressGlob.Compile)];
        _exclude ??= [.. Exclude.Select(AddressGlob.Compile)];
        return _match.Any(r => r.IsMatch(address)) && !_exclude.Any(r => r.IsMatch(address));
    }
}

/// <summary>A mod page's frontmatter, plus the feature list it publishes.</summary>
public sealed class LegacyModPage
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Url { get; set; }
    public string Archive { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    public string? Baseline { get; set; }
    public string? Analyzed { get; set; }

    /// <summary>Every bullet under the page's "Published feature list" heading, verbatim.</summary>
    [YamlIgnore]
    public List<string> Claims { get; set; } = [];
}

/// <summary>What <see cref="LegacyCatalog.Check"/> found. Clean means every change and every
/// published claim is accounted for exactly once.</summary>
public sealed record LegacyCheck(
    int Changes,
    IReadOnlyList<LegacyChange> Unclaimed,
    IReadOnlyList<(LegacyChange Change, IReadOnlyList<string> Features)> Contested,
    IReadOnlyList<string> UnclaimedClaims,
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, int> PerFeature)
{
    public bool Clean => Unclaimed.Count == 0 && Contested.Count == 0 && UnclaimedClaims.Count == 0 && Problems.Count == 0;
}

/// <summary>
/// A legacy mod's database: <c>mod.md</c> and its <c>features/*.md</c> pages, whose frontmatter
/// says which changes each feature owns.
/// </summary>
public sealed class LegacyCatalog
{
    public const string ModPage = "mod.md";
    public const string FeatureFolder = "features";
    private const string ClaimsHeading = "## published feature list";

    public LegacyModPage Mod { get; }
    public IReadOnlyList<LegacyFeature> Features { get; }

    private LegacyCatalog(LegacyModPage mod, IReadOnlyList<LegacyFeature> features)
    {
        Mod = mod;
        Features = features;
    }

    public static LegacyCatalog Load(string modDir)
    {
        string modPath = Path.Combine(modDir, ModPage);
        (string modYaml, string modBody) = Split(modPath);
        LegacyModPage mod = Parse<LegacyModPage>(modYaml, modPath);
        mod.Claims = ClaimsOf(modBody);

        string featureDir = Path.Combine(modDir, FeatureFolder);
        List<LegacyFeature> features = [];
        if (Directory.Exists(featureDir))
        {
            foreach (string page in Directory.EnumerateFiles(featureDir, "*.md").Order(StringComparer.Ordinal))
            {
                LegacyFeature feature = Parse<LegacyFeature>(Split(page).Yaml, page);
                feature.Id = Path.GetFileNameWithoutExtension(page);
                features.Add(feature);
            }
        }

        return new LegacyCatalog(mod, features);
    }

    public LegacyFeature? Find(string id) => Features.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Which change-claiming features each change matches.</summary>
    public List<(LegacyChange Change, List<LegacyFeature> Owners)> Attribute(IEnumerable<LegacyChange> changes)
    {
        LegacyFeature[] claiming = [.. Features.Where(f => f.ClaimsChanges)];
        return [.. changes.Select(c => (c, claiming.Where(f => f.Matches(c.Address)).ToList()))];
    }

    /// <summary>
    /// The features <paramref name="ids"/> name, with each bundle expanded to its components and
    /// everything they require, in dependency order.
    /// </summary>
    public List<LegacyFeature> Expand(IEnumerable<string> ids)
    {
        List<LegacyFeature> result = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id)
        {
            if (!seen.Add(id))
            {
                return;
            }

            LegacyFeature feature = Find(id) ?? throw new KeyNotFoundException($"No feature '{id}' in {Mod.Name}.");
            foreach (string required in feature.Requires.Concat(feature.Includes))
            {
                Visit(required);
            }

            if (feature.ClaimsChanges)
            {
                result.Add(feature);
            }
        }

        foreach (string id in ids)
        {
            Visit(id);
        }

        return result;
    }

    public LegacyCheck Check(IReadOnlyList<LegacyChange> changes, string? analyzedSha256)
    {
        List<string> problems = [];
        if (Mod.Sha256 is { } expected && analyzedSha256 is { } actual && !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"mod.md names archive sha256 {expected}, but the work folder was analyzed from {actual}.");
        }

        var published = new HashSet<string>(Mod.Claims, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(Features.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        foreach (LegacyFeature feature in Features)
        {
            problems.AddRange(Validate(feature, published, ids).Select(p => $"{feature.Id}: {p}"));
            claimed.UnionWith(feature.Claims);
        }

        List<(LegacyChange Change, List<LegacyFeature> Owners)> attribution = Attribute(changes);
        var perFeature = Features.ToDictionary(f => f.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (LegacyFeature owner in attribution.SelectMany(a => a.Owners))
        {
            perFeature[owner.Id]++;
        }

        foreach (LegacyFeature feature in Features.Where(f => f.Kind == "component"))
        {
            int count = perFeature[feature.Id];
            if (count == 0 && feature.Status is "located" or "partial")
            {
                problems.Add($"{feature.Id}: is {feature.Status} but its rules match no change.");
            }
            else if (count > 0 && feature.Status is "no-artifact" or "unresolved")
            {
                problems.Add($"{feature.Id}: is {feature.Status} but its rules match {count} change(s).");
            }
        }

        // A whole unit is written as one file, so features splitting it cannot be picked apart.
        foreach (IGrouping<string, (LegacyChange Change, List<LegacyFeature> Owners)> unit in attribution
                     .Where(a => a.Change.Whole && a.Owners.Count == 1)
                     .GroupBy(a => a.Change.Unit))
        {
            string[] owners = [.. unit.Select(a => a.Owners[0].Id).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (owners.Length > 1)
            {
                problems.Add($"{unit.Key} can only be taken whole, but {string.Join(", ", owners)} each claim part of it.");
            }
        }

        return new LegacyCheck(
            changes.Count,
            [.. attribution.Where(a => a.Owners.Count == 0).Select(a => a.Change)],
            [.. attribution.Where(a => a.Owners.Count > 1).Select(a => (a.Change, (IReadOnlyList<string>)[.. a.Owners.Select(f => f.Id)]))],
            [.. Mod.Claims.Where(c => !claimed.Contains(c))],
            problems,
            perFeature);
    }

    private static IEnumerable<string> Validate(LegacyFeature feature, HashSet<string> published, HashSet<string> ids)
    {
        if (feature.Title.Length == 0)
        {
            yield return "has no title.";
        }

        if (!LegacyFeature.Kinds.Contains(feature.Kind))
        {
            yield return $"kind '{feature.Kind}' is not one of {string.Join(", ", LegacyFeature.Kinds)}.";
        }

        if (feature.Kind == "component" && !LegacyFeature.Statuses.Contains(feature.Status))
        {
            yield return $"status '{feature.Status}' is not one of {string.Join(", ", LegacyFeature.Statuses)}.";
        }

        if (feature.Verified is { } verified && !LegacyFeature.Verifications.Contains(verified))
        {
            yield return $"verified '{verified}' is not one of {string.Join(", ", LegacyFeature.Verifications)}.";
        }

        if (feature.Kind == "bundle" && (feature.Match.Count > 0 || feature.Includes.Count == 0))
        {
            yield return "a bundle names its components in includes and matches no changes itself.";
        }

        if (feature.Kind != "bundle" && feature.Includes.Count > 0)
        {
            yield return "only a bundle includes other features; a dependency belongs in requires.";
        }

        foreach (string claim in feature.Claims.Where(c => !published.Contains(c)))
        {
            yield return $"claims \"{claim}\", which is not a line of mod.md's published feature list.";
        }

        foreach (string reference in feature.Requires.Concat(feature.Includes).Append(feature.Bundle).OfType<string>()
                     .Where(r => !ids.Contains(r)))
        {
            yield return $"refers to '{reference}', which has no page.";
        }

        foreach (string glob in feature.Match.Concat(feature.Exclude))
        {
            string? error = null;
            try
            {
                AddressGlob.Compile(glob);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
            }

            if (error is not null)
            {
                yield return $"rule '{glob}' does not compile: {error}";
            }
        }
    }

    private static (string Yaml, string Body) Split(string path)
    {
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal) || text.IndexOf("\n---\n", 3, StringComparison.Ordinal) is var end && end < 0)
        {
            throw new InvalidDataException($"{path} has no frontmatter between --- lines.");
        }

        return (text[4..(end + 1)], text[(end + 5)..]);
    }

    private static T Parse<T>(string yaml, string path)
    {
        try
        {
            return new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<T>(yaml) ?? throw new InvalidDataException($"{path} has empty frontmatter.");
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }

    private static List<string> ClaimsOf(string body)
    {
        List<string> claims = [];
        bool inList = false;
        foreach (string line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inList = line.Trim().Equals(ClaimsHeading, StringComparison.OrdinalIgnoreCase);
            }
            else if (inList && line.StartsWith("- ", StringComparison.Ordinal))
            {
                claims.Add(line[2..].Trim());
            }
        }

        return claims;
    }
}

/// <summary>
/// Change-address globs: <c>**</c> spans anything, <c>*</c> and <c>?</c> stay inside one
/// <c>/</c>-separated segment, <c>{a,b}</c> is either. Case-insensitive, and anchored at both ends.
/// </summary>
public static class AddressGlob
{
    public static Regex Compile(string glob)
    {
        var pattern = new System.Text.StringBuilder("^");
        int depth = 0;
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                bool slashAfter = i + 2 < glob.Length && glob[i + 2] == '/';
                pattern.Append(slashAfter ? "(?:.*/)?" : ".*");
                i += slashAfter ? 2 : 1;
            }
            else if (c == '*')
            {
                pattern.Append("[^/]*");
            }
            else if (c == '?')
            {
                pattern.Append("[^/]");
            }
            else if (c == '{')
            {
                depth++;
                pattern.Append("(?:");
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
                pattern.Append(')');
            }
            else if (c == ',' && depth > 0)
            {
                pattern.Append('|');
            }
            else
            {
                pattern.Append(Regex.Escape(c.ToString()));
            }
        }

        if (depth != 0)
        {
            throw new ArgumentException($"unbalanced braces in '{glob}'");
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
