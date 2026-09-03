using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>What kind of grouping a fragment sits in.</summary>
public enum FragmentParentKind
{
    MissionLayer,
    LibraryGroup,
}

/// <summary>
/// The structural grouping one fragment lives in - which mission layer a placed entity is nested
/// under, or which group declares an archetype. A fragment id deliberately carries none of this, so
/// an override always lands wherever the base container already put it.
/// </summary>
/// <param name="DeclaredPathId">
/// For a placed entity, the layer its own mission component claims, or null when it has none. The
/// engine reads an absent component as <c>main</c>.
/// </param>
public sealed record FragmentAncestry(
    FragmentParentKind Kind, string ParentName, uint? ParentPathId, uint? DeclaredPathId)
{
    /// <summary>
    /// Whether this entity's mission component names a different layer than the one it is nested
    /// under. The nesting wins - it is what the engine spawns from - so this is a silently wrong
    /// edit rather than a working one.
    /// </summary>
    public bool IsLayerMismatch
        => Kind == FragmentParentKind.MissionLayer
        && (DeclaredPathId is { } declared
            ? ParentPathId != declared
            : !MissionLayers.IsMain(ParentName));

    /// <summary>What this container calls the thing a fragment sits in.</summary>
    public string Grouping
        => Kind == FragmentParentKind.MissionLayer ? "mission layer" : "library group";

    public string Display => Kind switch
    {
        FragmentParentKind.MissionLayer when ParentPathId is { } id && !MissionLayers.IsMain(ParentName)
            => $"{Grouping} \"{ParentName}\" ({id:X8})",
        _ => $"{Grouping} \"{ParentName}\"",
    };
}

/// <summary>One decoded container, ready to have its fragments read.</summary>
/// <remarks>
/// Decoding is separated from reading because a container is decoded once and then asked for many
/// fragments - the entity library is 6 MB and a mod routinely overrides a dozen entries in it.
/// </remarks>
public interface IContainerTree
{
    /// <summary>This fragment's canonical XML, or null when the container holds no such fragment -
    /// which is not an error, but a layer adding new content rather than overriding.</summary>
    string? Extract(string fragmentId);

    /// <summary>
    /// Every fragment this container splits into, with the size to show against it - what the file
    /// browser lists under the container, and the set a mod picks from when staging an override.
    /// </summary>
    IReadOnlyList<FcbFragmentInfo> List();

    /// <summary>
    /// The container with every fragment reduced to a marker naming it, and fragments outside
    /// <paramref name="keep"/> dropped entirely. Null when this format does not compare by shape.
    /// </summary>
    /// <remarks>
    /// Two skeletons compare equal exactly when everything <em>around</em> the fragments matches and
    /// every common fragment sits in the same place - which is what tells an importer whether a
    /// container's whole change is expressible as per-fragment overrides. <paramref name="keep"/> is
    /// the ancestor's id set, so content a mod <em>adds</em> vanishes from the comparison: an
    /// addition is expressible as an appended override, a deletion is not.
    /// </remarks>
    string? Skeleton(Func<string, bool> keep);

    /// <summary>
    /// Where this fragment sits in the container, or null when the fragment is unknown or the format
    /// has no grouping to report. Display and diagnosis only - nothing in the override path consults
    /// it.
    /// </summary>
    FragmentAncestry? AncestryOf(string fragmentId) => null;

    /// <summary>
    /// The extra override unit this container needs, beyond its fragments, to differ from
    /// <paramref name="ancestor"/> the way it does - a world sector's mission-layer placement being
    /// the one that exists. Null when the format has no such unit, or nothing structural differs.
    /// </summary>
    (string Id, string Xml)? StructuralOverride(IContainerTree ancestor) => null;
}

/// <summary>
/// How one container format splits into individually overridable fragments.
/// </summary>
/// <remarks>
/// A mod stages a fragment at <c>&lt;container&gt;.&lt;ext&gt;\&lt;fragment id&gt;</c> and the build
/// merges it into the vanilla container rather than shipping the whole recompiled file. That
/// addressing is format-agnostic already - the id is a plain string keyed by container hash - so this
/// is the only piece a new format has to supply. See docs/design/mod-layout-final.md.
/// </remarks>
public interface IContainerSplitter
{
    IContainerTree Open(byte[] container);

    /// <summary>A staged fragment normalised into the shape <see cref="IContainerTree.Extract"/>
    /// emits, so a three-way merge compares like with like rather than diffing formatting. The id
    /// says which kind of unit this is, for a format whose reserved ids are not all one document
    /// shape.</summary>
    string Canonicalize(string fragmentId, string fragmentXml);

    /// <summary>The container's bytes with these fragments spliced in, ids not already present being
    /// added as new content.</summary>
    byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById);

    /// <summary>
    /// Folds two layers' versions of one fragment against their common ancestor. Text by default,
    /// which is right for anything whose lines are independent; a format with an override unit whose
    /// meaning is not line-shaped overrides this. The merged text is always usable as-is, even when
    /// the fold conflicted - the flag says a decision was made, not that the result is unfinished.
    /// </summary>
    (string Merged, bool Conflict) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => TextMerge(ancestor, ours, theirs);

    /// <summary>Line-based three-way merge, resolved by load order rather than left carrying
    /// conflict markers - which are not a container and cannot be built.</summary>
    static (string Merged, bool Conflict) TextMerge(string ancestor, string ours, string theirs)
    {
        (string merged, bool conflict) = Diff3.Merge(ancestor, ours, theirs);
        return conflict ? (theirs, true) : (merged, false);
    }

    /// <summary>
    /// Fragments of one container whose resolved overrides contradict each other, which no merge of
    /// a single fragment can notice because the contradiction is between two of them. Empty for a
    /// format whose override units are all independent, which is all of them but one.
    /// </summary>
    /// <remarks>
    /// <see cref="Apply"/> already resolves these the safe way on its own; naming them is what lets
    /// a build say so rather than quietly picking.
    /// </remarks>
    IReadOnlyList<(string FragmentId, string Kept, string Overruled)> Contradictions(
        IReadOnlyDictionary<string, string> resolved) => [];
}
