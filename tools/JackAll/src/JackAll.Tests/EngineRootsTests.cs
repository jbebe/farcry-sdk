using JackAll.Tools.Reach;

namespace JackAll.Tests;

/// <summary>The roots-asset parser: line format, flag grammar, and the match semantics the
/// reachability engine builds on.</summary>
public sealed class EngineRootsTests
{
    [Fact]
    public void Parses_every_kind_and_flag_spelling()
    {
        var roots = EngineRoots.Parse(
        [
            "# comment",
            "",
            "literal\tGLOBAL\tconfig\\alwaysloaded.xml",
            "pattern\tSP|MP\t^ui\\\\hud\\.mgb$",
            "world\tMP\tmp_[a-z0-9_]+",
            "family\tGLOBAL\tsource:shadersobj",
            "family\tEDITOR\tprefix:ingameeditor\\",
            "fallback\tNONE\t^worlds\\\\.*_depload\\.xml$\tfallback:primary-present",
        ]);

        Assert.Equal(6, roots.Rules.Count);
        RootMatch literal = roots.Match("config\\alwaysloaded.xml", "common");
        Assert.Equal(ReachFlagsExtensions.Global, literal.Flags);
        Assert.Equal("root:literal", literal.Reason);

        RootMatch pattern = roots.Match("ui\\hud.mgb", "common");
        Assert.Equal(ReachFlags.SP | ReachFlags.MP, pattern.Flags);

        RootMatch family = roots.Match("_unknown\\shader\\0badf00d.bin", "shadersobj");
        Assert.Equal(ReachFlagsExtensions.Global, family.Flags);

        RootMatch prefix = roots.Match("ingameeditor\\object_inventory.xml", "common");
        Assert.Equal(ReachFlags.Editor, prefix.Flags);

        RootMatch fallback = roots.Match("worlds\\world1\\world1_depload.xml", "worlds");
        Assert.Equal(ReachFlags.None, fallback.Flags);
        Assert.Equal("fallback:primary-present", fallback.FallbackReason);
    }

    [Fact]
    public void A_world_pattern_resolves_flags_through_the_world_rules()
    {
        var roots = EngineRoots.Parse(
        [
            "world\tSP\tworld[12]",
            "world\tNONE\ttmpla\tdev",
            "pattern\tWORLD\t^worlds\\\\(?<world>[^\\\\]+)\\\\mapcompass\\.xbt$",
        ]);

        Assert.Equal(ReachFlags.SP, roots.Match("worlds\\world2\\mapcompass.xbt", "worlds").Flags);
        RootMatch suppressed = roots.Match("worlds\\tmpla\\mapcompass.xbt", "worlds");
        Assert.Equal(ReachFlags.None, suppressed.Flags);
        Assert.Equal("dev", suppressed.SuppressedReason);

        RootMatch unknown = roots.Match("worlds\\mystery\\mapcompass.xbt", "worlds");
        Assert.Equal(ReachFlags.None, unknown.Flags);
        Assert.Equal(["mystery"], unknown.UnknownWorldTokens);
    }

    [Fact]
    public void Unmatched_rules_are_reported_after_a_sweep()
    {
        var roots = EngineRoots.Parse(
        [
            "literal\tGLOBAL\tconfig\\alwaysloaded.xml",
            "literal\tGLOBAL\tconfig\\never_shipped.xml",
        ]);
        roots.Match("config\\alwaysloaded.xml", "common");

        RootRule unmatched = Assert.Single(roots.UnmatchedRules());
        Assert.Equal("config\\never_shipped.xml", unmatched.Value);
    }

    [Theory]
    [InlineData("literal\tGLOBAL")]
    [InlineData("literal\tBOGUS\tx.xml")]
    [InlineData("nonsense\tGLOBAL\tx.xml")]
    [InlineData("family\tGLOBAL\tshadersobj")]
    [InlineData("literal\tWORLD\tx.xml")]
    public void Rejects_malformed_lines(string line)
        => Assert.ThrowsAny<InvalidDataException>(() => EngineRoots.Parse([line]));
}
