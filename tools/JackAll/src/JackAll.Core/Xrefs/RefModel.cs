namespace JackAll.Core.Xrefs;

/// <summary>
/// The namespace a hash lives in. A bare <c>u32</c> is meaningless on its own here: the game's path
/// hashes and its name hashes are the *same function* (both CRC32 over ASCII bytes - see
/// <see cref="Format.NameHash.Compute"/> vs <see cref="Format.Fcb.FcbClassDefinitions.Crc32Ascii"/>,
/// which differ only in whether the string is lowercased and slash-normalized first), so the two
/// collide numerically without being related at all. The remaining spaces aren't CRC32 in the first
/// place - they're the engine's own per-subsystem id counters.
/// </summary>
public enum RefSpace : byte
{
    /// <summary><see cref="Format.NameHash.Compute"/> of a game-relative path - a
    /// <see cref="Vfs.VfsFile"/>'s own <see cref="Vfs.VfsFile.Hash"/>.</summary>
    FilePath = 0,

    /// <summary><see cref="Format.Fcb.FcbClassDefinitions.Crc32Ascii"/> of a raw, case-sensitive
    /// name - an `.fcb` <c>Hash</c>-typed value (a bone, tag, material or bark-event name), an
    /// `.mgb` object id/page tag/UserData key, an `.xbg` material name. Often unresolvable: nothing
    /// in the shipped data stores the name itself, only its hash.</summary>
    EngineName = 1,

    /// <summary>A localised-string id, as authored in an `.mgb`'s
    /// <c>StringResourceExternalId</c> (<c>TABLEID</c>/<c>RESOURCEID</c>) and resolved through
    /// <c>languages\english\oasisstrings.rml</c>.</summary>
    OasisString = 2,

    /// <summary>An `.spk`/`.sbao` record id - the engine's own audio-resource identifier, not a name
    /// hash (see docs/docs/file-formats/spk.md).</summary>
    SoundResource = 3,

    /// <summary>A `depload.dat` per-resource *type* hash, from that file's own small deduplicated
    /// type table (see <see cref="Format.DepLoadChild.TypeHash"/>). Its semantic meaning isn't
    /// confirmed; it's indexed because grouping every dependency of one type is useful even while
    /// the type itself is anonymous.</summary>
    DepLoadType = 4,
}

/// <summary>
/// What a reference *is*, for display and filtering. Deliberately finer-grained than
/// <see cref="RefSpace"/>: two edges can land in the same space for completely different reasons
/// (an `.fcb` string path and a path scraped out of a `.lua` both target <see cref="RefSpace.FilePath"/>),
/// and an xref list that can't tell them apart is much harder to read.
/// </summary>
public enum RefKind : byte
{
    /// <summary>An `.fcb` <c>String</c> value that is a game-relative path.</summary>
    FcbPathValue = 0,

    /// <summary>An `.fcb` <c>Hash</c>/<c>HashArray</c> value.</summary>
    FcbNameValue = 1,

    /// <summary>A `depload.dat` parent's dependency on a child resource.</summary>
    DepLoadDependency = 2,

    /// <summary>The type tag a `depload.dat` child carries.</summary>
    DepLoadTypeTag = 3,

    /// <summary>An `.xbm` material's texture-slot binding to an `.xbt`.</summary>
    XbmTexture = 4,

    /// <summary>An `.xbg` mesh's material name.</summary>
    XbgMaterial = 5,

    /// <summary>An `.mgb` material's texture path.</summary>
    MgbTexture = 6,

    /// <summary>
    /// Any `.mgb` <c>NameId</c> - a <c>FullLink</c> target, an <c>AreaLink</c>'s package or area, an
    /// element tag, a <c>UserData</c> property key, a record's own name. Deliberately one kind
    /// rather than several: the package is walked through the format's own
    /// <c>IMgbCodec</c> visitor (see <c>MgbReferenceExtractor</c>), which reports the *field* a hash
    /// came from - and the field name, carried as the edge's site, is what actually distinguishes
    /// these. A per-case enum would have to be maintained by hand against every widget class and
    /// would go stale the moment one changed.
    /// </summary>
    MgbNameId = 7,

    /// <summary>An `.mgb` localised-string reference.</summary>
    MgbStringResource = 8,

    /// <summary>An `.spk` <c>SimpleFixed68</c> record's <c>LinkedId</c>.</summary>
    SpkRecordLink = 9,

    /// <summary>An `.spk` <c>SimpleFixed68</c> record's <c>CategoryId</c>.</summary>
    SpkCategory = 10,

    /// <summary>An `.spk` <c>TransformedFixed128</c> record's paired <c>FlatCopy</c> sibling.</summary>
    SpkFlatCopySibling = 11,

    /// <summary>A path found in the text of an `.xml`/`.lua`/`.rml`/`.mgb.desc` file.</summary>
    TextPath = 12,

    /// <summary>An `.xbt` header's embedded path to its `_mip0.xbt` streaming companion.</summary>
    XbtMipCompanion = 13,

    /// <summary>A MOVE graph's <c>m_animNameHash</c> clip reference - the path hash of a `.mab`.</summary>
    MoveClip = 14,

    /// <summary>An `.rtx` species' material slot, rewritten from the authoring `.mlm` to the
    /// `.xbm` that actually ships.</summary>
    RtxMaterial = 15,
}

/// <summary>Facts about a <see cref="RefKind"/> that the UI needs but the enum can't carry.</summary>
public static class RefKinds
{
    /// <summary>
    /// Whether this kind's <see cref="RefEdge.SiteKey"/> is a <see cref="RefSpace.FilePath"/> hash
    /// rather than a name hash.
    /// </summary>
    /// <remarks>
    /// A `depload.dat` has no field names to site an edge against - its structure is parents and
    /// children, both identified by resource hash - so the natural "where in this file" answer is the
    /// parent's own hash. That makes it the one kind whose site resolves through the filelist instead
    /// of the reference index's name table; without this distinction those rows render as raw
    /// <c>#XXXXXXXX</c> even when the filelist knows the path perfectly well.
    /// </remarks>
    public static bool SiteIsFileHash(RefKind kind)
        => kind is RefKind.DepLoadDependency or RefKind.DepLoadTypeTag;
}

/// <summary>
/// One reference: a file points at a hash in some space.
/// </summary>
/// <remarks>
/// The source is always a *file*, never an arbitrary node, because every reference physically lives
/// inside some file's bytes. That keeps the graph a plain "file → typed hash" relation, which is
/// exactly the shape both query directions need and avoids a second node table just to describe
/// where an edge starts.
///
/// The reference's **site** is stored structurally rather than as display text:
/// <see cref="SiteKey"/> is the member/property name hash (or a format-specific key - an `.spk`
/// record id, a texture slot's name hash) and <see cref="SiteIndex"/> its ordinal within that slot
/// for array-valued members. Rendering that to something readable
/// ("<c>CProjectile/fileMuzzleFx</c>") is <see cref="ReferenceSiteText"/>'s job at query time. At
/// ~52,000 `.fcb` files the edge count runs to millions, and a per-edge string would dominate the
/// index outright; deferring names to a dictionary is also what the rest of this codebase already
/// does everywhere else (see <see cref="Format.Fcb.FcbClassDefinitions"/>).
/// </remarks>
public readonly record struct RefEdge(
    uint SourceFile,
    RefSpace TargetSpace,
    uint Target,
    RefKind Kind,
    uint SiteKey,
    ushort SiteIndex);

/// <summary>
/// Where a hash is *defined*, as opposed to referenced.
/// </summary>
/// <remarks>
/// For <see cref="RefSpace.FilePath"/> this is redundant - the VFS entry with that hash is the
/// definition - but for every other space it's the only thing that makes "jump to it" possible at
/// all: a <see cref="RefSpace.SoundResource"/> id is defined by whichever `.spk` bank contains that
/// record, and without this table a double-click on one has nowhere to go. Not every id has a
/// definition (an <see cref="RefSpace.EngineName"/> usually has none anywhere in the shipped data),
/// which is why this is a separate sparse table rather than a field on an edge.
/// </remarks>
public readonly record struct RefDefinition(
    RefSpace Space,
    uint Id,
    uint DefiningFile,
    uint SiteKey);
