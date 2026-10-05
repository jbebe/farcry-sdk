using JackAll.Core.Legacy;

namespace JackAll.Tests;

/// <summary>
/// The check that makes a legacy mod's database complete: every change claimed once, every
/// published claim covered, every page's references real.
/// </summary>
public class LegacyCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jackall-legacy-catalog", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private LegacyCatalog Catalog(params (string Id, string Frontmatter)[] pages)
    {
        Directory.CreateDirectory(Path.Combine(_dir, LegacyCatalog.FeatureFolder));
        File.WriteAllText(Path.Combine(_dir, LegacyCatalog.ModPage), """
            ---
            name: Test mod
            archive: test.zip
            ---

            ## Published feature list

            ### Gameplay

            - Faster cars
            - Tougher cars
            """);
        foreach ((string id, string frontmatter) in pages)
        {
            File.WriteAllText(Path.Combine(_dir, LegacyCatalog.FeatureFolder, id + ".md"), $"---\n{frontmatter}\n---\n\nBody.\n");
        }

        return LegacyCatalog.Load(_dir);
    }

    private static readonly LegacyChange[] Changes =
    [
        new(ChangeKind.Field, "lib.fcb/car.xml", "lib.fcb/car.xml#Engine/fSpeed", "30", "60"),
        new(ChangeKind.Field, "lib.fcb/car.xml", "lib.fcb/car.xml#Parts/fHealth", "500", "1000"),
        new(ChangeKind.New, "_hash/0badf00d.lua", "_hash/0badf00d.lua", null, "10 bytes", Whole: true),
    ];

    [Fact]
    public void A_database_claiming_each_change_once_and_covering_each_claim_is_clean()
    {
        LegacyCatalog catalog = Catalog(
            ("fast", "title: Fast\nstatus: located\nclaims: [\"Faster cars\"]\nmatch: [\"**#**/fSpeed\", \"_hash/**\"]"),
            ("tough", "title: Tough\nstatus: located\nclaims: [\"Tougher cars\"]\nmatch: [\"**#**/fHealth\"]"),
            ("cars", "title: Cars\nkind: bundle\nincludes: [fast, tough]"));

        LegacyCheck check = catalog.Check(Changes, null);

        Assert.True(check.Clean, string.Join("; ", check.Problems));
        Assert.Equal(["fast", "tough"], catalog.Expand(["cars"]).Select(f => f.Id));
    }

    [Fact]
    public void Overlapping_rules_contest_a_change_and_an_uncovered_claim_is_reported()
    {
        LegacyCatalog catalog = Catalog(
            ("fast", "title: Fast\nstatus: located\nclaims: [\"Faster cars\"]\nmatch: [\"lib.fcb/**\", \"_hash/**\"]"),
            ("tough", "title: Tough\nstatus: located\nmatch: [\"**#**/fHealth\"]"));

        LegacyCheck check = catalog.Check(Changes, null);

        Assert.Equal("lib.fcb/car.xml#Parts/fHealth", Assert.Single(check.Contested).Change.Address);
        Assert.Equal(["Tougher cars"], check.UnclaimedClaims);
        Assert.False(check.Clean);
    }

    [Fact]
    public void A_page_is_held_to_its_status_its_claims_and_its_references()
    {
        LegacyCatalog catalog = Catalog(
            ("fast", "title: Fast\nstatus: located\nclaims: [\"Faster cars!\"]\nrequires: [missing]\nmatch: [\"nothing/**\"]"));

        LegacyCheck check = catalog.Check(Changes, null);

        Assert.Contains(check.Problems, p => p.Contains("not a line of mod.md", StringComparison.Ordinal));
        Assert.Contains(check.Problems, p => p.Contains("'missing'", StringComparison.Ordinal));
        Assert.Contains(check.Problems, p => p.Contains("match no change", StringComparison.Ordinal));
    }
}
