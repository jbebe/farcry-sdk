using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Runs the real override chain over the real shipped libraries: a world's own base plus the patch
/// override that ships specifically to shadow it. The counts asserted here come from the engine-side
/// write-up (docs/docs/engine-internals/entity-instancing.md), so a regression in the walk shows up
/// as a count mismatch rather than as quietly missing archetypes.
/// </summary>
public class ArchetypeIndexTests
{
    private const string FixturesDir = "Fixtures/Fcb";
    private const string BasePath = @"worlds\world1\generated\entitylibrary.fcb";
    private const string PatchPath = @"generated\entitylibrarypatchoverride.fcb";

    /// <summary>Names both fixtures declare, so the patch override shadows the base's copy.</summary>
    private const int ContestedNames = 160;

    private static readonly Dictionary<string, string> LayerFixtures = new(StringComparer.OrdinalIgnoreCase)
    {
        [BasePath] = "worlds_entitylibrary.fcb",
        [PatchPath] = "patch_entitylibrarypatchoverride.fcb",
    };

    private static bool FixturesPresent
        => LayerFixtures.Values.All(f => File.Exists(Path.Combine(FixturesDir, f)));

    /// <summary>Stands in for the VFS: resolves only the two layers under test, misses everything else.</summary>
    private static byte[]? ReadFixture(string path)
        => LayerFixtures.TryGetValue(path, out string? file)
            ? File.ReadAllBytes(Path.Combine(FixturesDir, file))
            : null;

    private static ArchetypeIndex LoadChain()
        => ArchetypeIndex.Load(ArchetypeIndex.LayerPaths("world1"), ReadFixture);

    private static ArchetypeIndex LoadSingle(string path)
        => ArchetypeIndex.Load([new ArchetypeLayer(path)], ReadFixture);

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            FixturesPresent,
            $"{FixturesDir} is missing {string.Join(" / ", LayerFixtures.Values)}, so every "
            + "fixture-backed test in this class silently no-opped.");

    /// <summary>The name hashes the whole resolution rests on, as read out of the shipped binaries.</summary>
    [Fact]
    public void The_field_and_object_name_hashes_match_the_engines()
    {
        Assert.Equal(0xB9295CC7u, FcbClassDefinitions.Crc32Ascii("hidName"));
        Assert.Equal(0xFE11D138u, FcbClassDefinitions.Crc32Ascii("Name"));
        Assert.Equal(0x0984415Eu, FcbClassDefinitions.Crc32Ascii("Entity"));
        Assert.Equal(0x0984415Eu, WorldHashes.Entity);
        Assert.Equal(0xB9295CC7u, WorldHashes.HidName);
    }

    /// <summary>
    /// 915 is the patch override's documented archetype count, so it cross-checks the walk against the
    /// engine-side measurement. The base's 650 is a property of this fixture, which is some world's
    /// library but not world1's (that one declares 1,419).
    /// </summary>
    /// <summary>A DLC library lives under its own folder plus a "generated" subfolder, so the naive
    /// parent-folder label would call every one of them "generated".</summary>
    [Fact]
    public void Each_layer_gets_a_distinguishing_short_name()
    {
        Assert.Equal("base", new ArchetypeLayer(BasePath).ShortName);
        Assert.Equal("full", new ArchetypeLayer(@"worlds\world1\generated\entitylibrary_full.fcb").ShortName);
        Assert.Equal("patch", new ArchetypeLayer(PatchPath).ShortName);
        Assert.Equal("dlc1", new ArchetypeLayer(@"downloadcontent\dlc1\generated\entitylibrary.fcb").ShortName);
        Assert.Equal("dlc_jungle", new ArchetypeLayer(@"downloadcontent\dlc_jungle\entitylibrary.fcb").ShortName);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_chain_resolves_the_expected_archetype_counts()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex chain = LoadChain();
        Assert.Equal(
            (650, 915, 1405, ContestedNames),
            (LoadSingle(BasePath).Count, LoadSingle(PatchPath).Count, chain.Count, chain.Overridden.Count()));
    }

    /// <summary>Every contested name resolves to the patch override, because it loads last.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_last_library_loaded_wins_every_contested_name()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = LoadChain();
        Assert.NotEmpty(index.Overridden);
        foreach (string name in index.Overridden)
        {
            IReadOnlyList<ArchetypeDefinition> chain = index.DefinitionsOf(name);
            Assert.Equal(BasePath, chain[0].Layer.Path);
            Assert.Equal(PatchPath, chain[^1].Layer.Path);
            Assert.Same(chain[^1], index.Winner(name));
        }
    }

    /// <summary>CNoCaseStringID: a name differing only in case is the same archetype.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Names_resolve_case_insensitively()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = LoadChain();
        string name = index.Names.First(n => n.Any(char.IsLetter));

        Assert.NotNull(index.Winner(name.ToUpperInvariant()));
        Assert.NotNull(index.Winner(name.ToLowerInvariant()));
    }

    /// <summary>A declaration carries the fragment a mod would have to override to change it.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_declaration_is_attributed_to_a_container_and_fragment()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = LoadChain();
        foreach (string name in index.Names)
        {
            foreach (ArchetypeDefinition definition in index.DefinitionsOf(name))
            {
                Assert.Equal(NameHash.Compute(definition.Layer.Path), definition.ContainerHash);
                Assert.False(string.IsNullOrEmpty(definition.FragmentId));
            }
        }
    }

    /// <summary>The lint primitive: the base's contested declarations are dead, the winner's are not.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Dead_declarations_are_the_shadowed_ones_only()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = LoadChain();
        uint baseHash = NameHash.Compute(BasePath);
        uint patchHash = NameHash.Compute(PatchPath);

        var deadFragments = index.Names
            .SelectMany(index.DefinitionsOf)
            .Where(d => d.ContainerHash == baseHash)
            .Select(d => d.FragmentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int dead = deadFragments.Sum(f => index.DeadDeclarationsIn(baseHash, f).Count());
        Assert.Equal(ContestedNames, dead);

        foreach (string? fragment in deadFragments)
        {
            Assert.Empty(index.DeadDeclarationsIn(patchHash, fragment));
        }
    }

    /// <summary>
    /// A variant is an independent copy of its stem, so it must sit beside it rather than under it -
    /// otherwise a stem shows up twice, as both a folder and its own leaf.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_variant_stays_beside_its_stem_instead_of_nesting_under_it()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = LoadChain();
        var declared = index.Names.ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? LongestDeclaredPrefix(string name)
        {
            string[] parts = name.Split('.');
            for (int k = parts.Length - 1; k > 0; k--)
            {
                string prefix = string.Join('.', parts, 0, k);
                if (declared.Contains(prefix)) return prefix;
            }
            return null;
        }

        string variant = index.Names.First(n => LongestDeclaredPrefix(n) is not null);
        string stem = LongestDeclaredPrefix(variant)!;

        (IReadOnlyList<string> stemGroups, string stemLabel) = index.SplitForDisplay(stem);
        (IReadOnlyList<string> groups, string label) = index.SplitForDisplay(variant);

        Assert.Equal(stemGroups, groups);
        Assert.Equal($"{stemLabel}{variant[stem.Length..]}", label);

        // A name with no declared prefix still splits all the way down to its last segment.
        string plain = index.Names.First(n => n.Contains('.') && LongestDeclaredPrefix(n) is null);
        (IReadOnlyList<string> plainGroups, string plainLabel) = index.SplitForDisplay(plain);
        Assert.Equal(plain.Split('.').Length - 1, plainGroups.Count);
        Assert.DoesNotContain('.', plainLabel);
    }

    /// <summary>
    /// The lint's whole job: an edit staged against the base library's contested fragments is reported,
    /// and the same edit staged against the patch override is not.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_lint_reports_edits_that_land_on_a_shadowed_declaration()
    {
        if (!FixturesPresent) return;

        // DiscoverWorlds has to find world1 for the lint to resolve any chain at all.
        string[] knownPaths = [BasePath, PatchPath];
        ArchetypeIndex index = LoadChain();
        uint baseHash = NameHash.Compute(BasePath);

        ArchetypeDefinition shadowed = index.Names
            .Select(index.DefinitionsOf)
            .First(c => c.Count > 1)[0];

        IReadOnlyList<DeadEdit> dead = ArchetypeLint.Run(
            [new StagedFragment("my-mod", baseHash, shadowed.FragmentId!)], knownPaths, ReadFixture,
            LibraryProfile.Server);

        Assert.Contains(shadowed.Name, dead.Select(d => d.Archetype));
        Assert.All(dead, d => Assert.Equal(PatchPath, d.WinningPath));
        Assert.All(dead, d => Assert.Equal("my-mod", d.Source));

        IReadOnlyList<DeadEdit> live = ArchetypeLint.Run(
            [new StagedFragment("my-mod", NameHash.Compute(PatchPath), shadowed.FragmentId!)],
            knownPaths, ReadFixture, LibraryProfile.Server);
        Assert.Empty(live);
    }

    /// <summary>
    /// The engine's own walk is exactly two levels - group, then prototype - so anything nested deeper
    /// would be indexed here and never instantiated in game.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Prototypes_sit_exactly_two_levels_below_the_library_root()
    {
        if (!FixturesPresent) return;

        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.fcb"))
        {
            FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(file));
            var depths = new HashSet<int>();
            CollectPrototypeDepths(root, 0, depths);

            Assert.Equal([2], depths);
        }
    }

    private static void CollectPrototypeDepths(FcbObject node, int depth, HashSet<int> depths)
    {
        if (node.TypeHash == WorldHashes.EntityPrototype)
        {
            depths.Add(depth);
            return;
        }
        foreach (FcbObject child in node.Children)
        {
            CollectPrototypeDepths(child, depth + 1, depths);
        }
    }
}
