using JackAll.Core.Format;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// Root detection is the whole reason <see cref="ModLayerInspector"/> exists, and it's a silent
/// failure when it's wrong — a mod with a misread root classifies to nothing, installs cleanly, and
/// simply doesn't apply. So the interesting cases are all about which prefix wins, with content
/// recognized only under the reserved mods\ folder and plugins under plugins\.
/// </summary>
public class ModLayerInspectorTests
{
    private const string RealFileA = @"engine\gamemodes\gamemodesconfig.xml";
    private const string RealFileB = @"config\inputactionmapcommon.xml";
    private const string Container = @"generated\entitylibrarypatchoverride.fcb";

    /// <summary>Stands in for the game's archives: only the two paths above (and nothing under any
    /// wrapper folder) are entries the engine actually has.</summary>
    private static readonly HashSet<uint> GameHashes =
        [NameHash.Compute(RealFileA), NameHash.Compute(RealFileB)];

    private static ModLayerReport Inspect(params string[] paths)
        => ModLayerInspector.Inspect(paths, GameHashes.Contains);

    /// <summary>Stands in for a game whose only entry is <paramref name="container"/> - a fragment's
    /// own path hashes to nothing, so it's the container that has to exist.</summary>
    private static ModLayerReport InspectIn(string container, params string[] paths)
        => ModLayerInspector.Inspect(paths, hash => hash == NameHash.Compute(container));

    [Fact]
    public void An_already_rooted_mod_keeps_the_top_level_as_its_root()
    {
        ModLayerReport report = Inspect($@"mods\{RealFileA}", $@"mods\{RealFileB}");

        Assert.Equal("", report.Root);
        Assert.Equal(2, report.WholeFileOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void Content_at_the_layer_root_is_ignored_not_unknown()
    {
        ModLayerReport report = Inspect(RealFileA, @"_hash\4a724578.xbt");

        // No root-layout fallback: outside mods\/plugins\ nothing is even hashed.
        Assert.Equal(0, report.TotalOverrides);
        Assert.Equal(2, report.IgnoredFiles);
    }

    [Fact]
    public void A_wrapper_folder_is_stripped()
    {
        ModLayerReport report = Inspect($@"MyCoolMod v1.2\mods\{RealFileA}", $@"MyCoolMod v1.2\mods\{RealFileB}");

        // Normalized, so lowercase - callers matching this back against real entry names have to do
        // it case-insensitively, which is how Windows paths compare anyway.
        Assert.Equal("mycoolmod v1.2", report.Root);
        Assert.Equal(2, report.WholeFileOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void Two_nested_wrapper_folders_are_stripped_together()
    {
        ModLayerReport report = Inspect($@"MyMod v1.2\MyMod\mods\{RealFileA}");

        Assert.Equal(@"mymod v1.2\mymod", report.Root);
        Assert.Equal(1, report.WholeFileOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void A_readme_beside_the_wrapper_does_not_drag_the_root_back_to_the_top()
    {
        ModLayerReport report = Inspect("readme.txt", $@"MyCoolMod\mods\{RealFileA}", $@"MyCoolMod\mods\{RealFileB}");

        Assert.Equal("mycoolmod", report.Root);
        // The readme sits outside the winning root, so it isn't counted at all - which is right: it
        // was never going to reach the engine either way.
        Assert.Equal(2, report.WholeFileOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void A_correctly_rooted_mod_is_never_pushed_down_into_one_of_its_own_folders()
    {
        // 'mods' itself is also a candidate root. Descending into it would score strictly worse
        // (the mods\ prefix is part of the contract, so its children recognize nothing), so the
        // tie-break must not take it.
        ModLayerReport report = Inspect($@"mods\{RealFileA}", $@"mods\{RealFileB}");

        Assert.Equal("", report.Root);
    }

    [Fact]
    public void Hash_addressed_entries_are_counted_and_still_resolve_without_a_recovered_name()
    {
        uint hash = NameHash.Compute(RealFileA);
        ModLayerReport report = Inspect($@"mods\_hash\{hash:x8}.xbt");

        Assert.Equal("", report.Root);
        Assert.Equal(1, report.WholeFileOverrides);
        Assert.Equal(1, report.HashAddressed);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void An_fcb_fragment_is_reported_separately_and_judged_by_its_container()
    {
        ModLayerReport report = InspectIn(Container, $@"mods\{Container}\vehicle\Land\Jeep.xml");

        Assert.Equal(0, report.WholeFileOverrides);
        Assert.Equal(1, report.FragmentOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    /// <summary>A removed-id-space override is an error wherever it is addressed from, so a report can
    /// never quietly count one as a working override.</summary>
    [Theory]
    [InlineData(@"mods\generated\entitylibrarypatchoverride.fcb\03_Foo.xml")]
    [InlineData(@"mods\_hash\1a2b3c4d.fcb\03_Foo.xml")]
    [InlineData(@"MyMod v1.2\mods\generated\entitylibrarypatchoverride.fcb\03_Foo.xml")]
    public void A_group_id_override_is_refused_rather_than_reported(string path)
        => Assert.Throws<InvalidDataException>(() => Inspect(path));

    [Fact]
    public void Two_spellings_of_one_entity_fragment_count_as_a_single_override()
    {
        // A placed entity's name prefix is cosmetic, so both spell the same override - counting two
        // would report more than the build actually merges (ModPathHashing.Add collapses them).
        const string sector = @"worlds\world1\generated\worldsectors\worldsector17.data.fcb";

        ModLayerReport report = InspectIn(sector,
            $@"mods\{sector}\Guard_12.2058514756624450165.xml", $@"mods\{sector}\2058514756624450165.xml");

        Assert.Equal(1, report.FragmentOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void A_hash_addressed_container_takes_a_nested_fragment_id_too()
    {
        ModLayerReport report = InspectIn(
            Container, $@"mods\_hash\{NameHash.Compute(Container):x8}.fcb\vehicle\Land\Jeep.xml");

        Assert.Equal(1, report.FragmentOverrides);
        Assert.Equal(1, report.HashAddressed);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void A_wrapper_above_a_deep_fragment_tree_is_stripped_without_descending_into_it()
    {
        // Every directory inside the container is also a candidate root; none of them may win.
        ModLayerReport report = InspectIn(Container, $@"MyMod v1.2\mods\{Container}\vehicle\Land\Jeep.xml");

        Assert.Equal("mymod v1.2", report.Root);
        Assert.Equal(1, report.FragmentOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void Something_that_is_not_a_mod_at_all_reports_nothing_recognizable()
    {
        ModLayerReport report = Inspect("readme.txt", @"screenshots\one.png");

        // Nothing sits under mods\ or plugins\, so nothing is hashed at all - "no recognized files"
        // is exactly the signal a caller uses to say "this isn't a Far Cry 2 mod".
        Assert.Equal(0, report.TotalOverrides);
        Assert.Equal(0, report.PluginFiles);
        Assert.Equal(2, report.IgnoredFiles);
    }

    [Fact]
    public void Plugin_files_are_recognized_side_content_not_unknown_entries()
    {
        ModLayerReport report = Inspect($@"mods\{RealFileA}", @"plugins\coolplugin.dll");

        Assert.Equal(1, report.WholeFileOverrides);
        Assert.Equal(1, report.PluginFiles);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void A_wrapper_above_plugins_and_content_is_stripped_together()
    {
        ModLayerReport report = Inspect($@"MyMod\mods\{RealFileA}", @"MyMod\plugins\x.dll");

        Assert.Equal("mymod", report.Root);
        Assert.Equal(1, report.WholeFileOverrides);
        Assert.Equal(1, report.PluginFiles);
    }

    [Fact]
    public void A_plugins_only_tree_reports_plugin_files_and_no_overrides()
    {
        ModLayerReport report = Inspect(@"plugins\a.dll", @"plugins\sub\b.lua");

        Assert.Equal(2, report.PluginFiles);
        Assert.Equal(0, report.TotalOverrides);
        Assert.Equal(0, report.UnknownEntries);
    }

    [Fact]
    public void Without_a_game_to_check_against_the_tree_is_reported_as_given()
    {
        ModLayerReport report = ModLayerInspector.Inspect([$@"MyCoolMod\mods\{RealFileA}"]);

        // No entryExists probe means no root scoring: the wrapper stays, and under it nothing
        // classifies. The caller is warned to pass --game for exactly this reason.
        Assert.Equal("", report.Root);
        Assert.Equal(0, report.TotalOverrides);
    }
}
