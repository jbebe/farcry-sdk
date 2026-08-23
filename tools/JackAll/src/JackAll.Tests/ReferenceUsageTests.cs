using JackAll.Core.Format;
using JackAll.Core.Xrefs;
using JackAll.Tools.Fc2Model;

namespace JackAll.Tests;

/// <summary>
/// Counting who uses a file, which is what decides whether a pack lets an editor change it.
/// </summary>
/// <remarks>
/// Built over synthetic edges rather than a real index, because the rule is about which *kinds* of
/// edge mean "uses" and that is not a fact about any particular install. What a real install says
/// is recorded where it was measured: of the 325 pooled materials the shipped weapons name, 140
/// have exactly one user and promote to <c>owned</c> - including the sawed-off shotgun's, which the
/// directory rule leaves shared and a modeler otherwise has to go and find by hand.
/// </remarks>
public sealed class ReferenceUsageTests
{
    private const string Material = "graphics/_materials/pooled.xbm";
    private const string Model = "graphics/weapons/primary/ak47/ak47.xbg";
    private const string Other = "graphics/weapons/secondary/deserteagle/deserteagle.xbg";
    private const string Level = "worlds/world1/generated/world1_depload.dat";

    /// <summary>
    /// A level's manifest is not a user, and the same fact restated as text is not a second one.
    /// </summary>
    /// <remarks>
    /// This is the whole rule. Every world ships a <c>_depload.dat</c> listing what it loads and a
    /// generated <c>.xml</c> twin restating it, so a material the ak47 alone uses is referenced by
    /// four dozen files - counting those makes nothing ever promote. Measured on the shipped rifle:
    /// 47 references, 8 users.
    /// </remarks>
    [Fact]
    public void A_level_loading_a_file_does_not_count_as_using_it()
    {
        ReferenceIndex index = Index(
            Edge(Level, Material, RefKind.DepLoadDependency, site: Model),
            Edge(Level.Replace(".dat", ".xml"), Material, RefKind.TextPath));

        Assert.Equal(1, Count(index, Material, Model));
    }

    /// <summary>
    /// A second model naming the same material is a user, wherever the index learned of it.
    /// </summary>
    /// <remarks>
    /// Both halves are needed. A mesh names its material in its own bytes; a level's manifest names
    /// no bytes but sites each dependency by the resource that pulled it in, and that parent is the
    /// only place the graph records a user for a file nothing else mentions.
    /// </remarks>
    [Theory]
    [InlineData(RefKind.XbgMaterial)]
    [InlineData(RefKind.FcbPathValue)]
    public void Another_model_naming_it_makes_it_shared(RefKind kind)
        => Assert.Equal(
            [NameHash.Compute(Model), NameHash.Compute(Other)],
            Users(Index(Edge(Other, Material, kind)), Material));

    /// <summary>
    /// A dependency the level's manifest sites against another model counts that model, not the
    /// level - the one place the graph knows a user that no file's own bytes name.
    /// </summary>
    /// <remarks>
    /// Asserted as the set rather than its size on purpose: counting the level instead of the site
    /// gives two either way, so a size check passes a rule that names entirely the wrong file.
    /// </remarks>
    [Fact]
    public void A_manifest_dependency_counts_the_model_it_is_sited_against()
        => Assert.Equal(
            [NameHash.Compute(Model), NameHash.Compute(Other)],
            Users(Index(Edge(Level, Material, RefKind.DepLoadDependency, site: Other)), Material));

    /// <summary>
    /// The model is counted whether or not the index holds its edge, so a count of one can only ever
    /// mean this model - never one other file with the model's own edge missing.
    /// </summary>
    [Fact]
    public void The_model_counts_even_when_the_index_never_saw_it()
    {
        Assert.Equal(1, Count(Index(Edge(Model, Material, RefKind.XbgMaterial)), Material, Model));
        Assert.Equal(1, Count(Index(), Material, Model));
    }

    /// <summary>
    /// An empty index yields no counter at all, rather than one that calls everything unused.
    /// </summary>
    /// <remarks>
    /// A counter returning zero would promote every file in a pack to <c>owned</c> and let an editor
    /// re-skin half the weapons in the game. Without counts the pack falls back to the directory
    /// rule, which is wrong in the safe direction.
    /// </remarks>
    [Fact]
    public void No_index_means_no_counts()
        => Assert.Null(ReferenceUsage.Counter(ReferenceIndex.Empty, Model));

    private static int Count(ReferenceIndex index, string path, string model)
        => ReferenceUsage.Counter(index, model)!(path);

    private static SortedSet<uint> Users(ReferenceIndex index, string path)
        => [.. ReferenceUsage.Users(index, path, NameHash.Compute(Model))];

    private static ReferenceIndex Index(params RefEdge[] edges)
        => ReferenceIndex.Build(
            edges.Length > 0 ? edges : [Edge(Model, "graphics/unrelated.xbm", RefKind.XbgMaterial)],
            [],
            new Dictionary<uint, string>(),
            []);

    private static RefEdge Edge(string source, string target, RefKind kind, string? site = null)
        => new(
            NameHash.Compute(source),
            RefSpace.FilePath,
            NameHash.Compute(target),
            kind,
            site is null ? 0 : NameHash.Compute(site),
            0);
}
