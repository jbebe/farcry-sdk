using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

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
    /// emits, so a three-way merge compares like with like rather than diffing formatting.</summary>
    string Canonicalize(string fragmentXml);

    /// <summary>The container's bytes with these fragments spliced in, ids not already present being
    /// added as new content.</summary>
    byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById);
}
