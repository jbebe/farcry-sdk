using JackAll.Tools.Mgb;

namespace JackAll.Tests;

/// <summary>
/// Tests <see cref="MgbVerify"/>, which is what a hand-authored package is checked with before it
/// is built (FCSE's CMake runs <c>mgb verify --page FCSE_PAGE</c> ahead of every encode).
/// </summary>
/// <remarks>
/// A checker of this kind has two ways to be useless, and both are tested here. It can cry wolf -
/// so a shipped package must come back clean, which is the only ground truth available for what a
/// valid package looks like. And it can be silently vacuous: every reference into another package
/// is skipped, so a rule that never matches anything would pass everything just as quietly. Hence a
/// shipped page is checked for what was resolved as well as for what was found.
/// </remarks>
public sealed class MgbVerifyTests
{
    private static readonly string FcsePageXml = Path.Combine(
        TestSupport.RepositoryRoot, "tools", "FCSE", "assets", "fcse.mgb.xml");

    [Theory]
    [MemberData(nameof(MgbRoundTripTests.Packages), MemberType = typeof(MgbRoundTripTests))]
    public void Finds_nothing_wrong_with_a_shipped_package(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        Assert.Empty(MgbVerify.Check(MgbPackage.Read(original)).Findings);
    }

    /// <summary>The other half of the shipped-package check: that the clean result above is a real
    /// one. Every rule here can decline to apply, so a walk that reached nothing would report the
    /// same nothing.</summary>
    [Fact]
    public void Resolves_hundreds_of_references_in_a_shipped_page()
    {
        if (Fixture.Read("Mgb/options.mgb") is not { } original) return;

        int resolved = MgbVerify.Check(MgbPackage.Read(original)).ReferencesChecked;

        // options.mgb resolves 265, far past anything a rule that quietly matched nothing could reach.
        Assert.True(resolved > 200, $"only {resolved} references were resolved in options.mgb");
    }

    [Fact]
    public void Accepts_the_fcse_page_package_as_a_reachable_page()
    {
        MgbVerifyResult result = Check(File.ReadAllText(FcsePageXml), "FCSE_PAGE");

        Assert.Empty(result.Findings);
        // Three banks of twenty, plus the chrome
        Assert.True(result.ReferencesChecked > 60);
    }

    /// <summary>The failure this whole check exists for: a link that names an element the package
    /// does not contain loads perfectly and produces a page with a control missing.</summary>
    [Fact]
    public void Reports_a_link_to_an_element_that_does_not_exist()
    {
        string xml = File.ReadAllText(FcsePageXml).Replace("p_slot_03 ", "p_slot_99 ");

        MgbFinding finding = Assert.Single(Check(xml, "FCSE_PAGE").Findings);

        // Names, not hashes: the source spells them out, and a report in hashes is one the author
        // has to go and decode before they can act on it.
        Assert.Contains("FCSE_SLOT_03", finding.Where);
        Assert.Contains("p_slot_99", finding.Problem);
    }

    /// <summary>An area nothing registers is authored, laid out, and unreachable -
    /// <c>GenericObjectServer::FindGenericObject</c> is the only way in.</summary>
    [Fact]
    public void Reports_a_page_that_no_registry_entry_is_keyed_under()
    {
        MgbFinding finding = Assert.Single(
            Check(File.ReadAllText(FcsePageXml), "MAINMENU_OPTIONS_PAGE_PC").Findings);

        Assert.Contains("MAINMENU_OPTIONS_PAGE_PC", finding.Where);
        Assert.Contains("GenericObjectTable", finding.Problem);
    }

    /// <summary>Cross-package references are the engine's to resolve against whatever else is
    /// loaded, so a single file must not be blamed for one it cannot see.</summary>
    [Fact]
    public void Says_nothing_about_a_link_into_another_package()
    {
        // Every FCSE_SLOT_ link's fourth id already names an area in common.mgb. Repointing the
        // first id at that package makes the whole chain someone else's to answer for.
        string xml = File.ReadAllText(FcsePageXml)
            .Replace("IDS=\"fcse FCSE_PAGE p_slot_05", "IDS=\"common FCSE_PAGE p_slot_05");

        Assert.Empty(Check(xml, "FCSE_PAGE").Findings);
    }

    private static MgbVerifyResult Check(string xml, params string[] pages)
    {
        // The same two steps the CLI takes: build the package the game would load, keeping the
        // names on the way past, then check what was built rather than what was written.
        var names = new MgbNameLookup();
        MgbPackage package = MgbPackage.Read(MgbXml.Encode(xml, names));
        return MgbVerify.Check(package, pages, names.Names);
    }
}
