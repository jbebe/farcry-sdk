using JackAll.Core.Xrefs;

namespace JackAll.Tools.Reach;

/// <summary>
/// The judgment calls of the reachability analysis, separated from the mechanics so each carries
/// its rationale. The propagation table deliberately differs from <see cref="Fc2Model.ReferenceUsage"/>
/// in one place: <see cref="RefKind.TextPath"/> propagates here, because a path named in a
/// *reachable* .lua/.xml can genuinely be loaded - and the noise that rule guards against
/// (the `_depload.xml` twins) self-corrects, since the twins are fallbacks that never become
/// reachable sources in the first place.
/// </summary>
public static class ReachPolicy
{
    /// <summary>Edge kinds whose target is a file the engine would load.</summary>
    public static bool PropagatesToFile(RefKind kind) => kind is
        RefKind.FcbPathValue or RefKind.XbmTexture or RefKind.MgbTexture or
        RefKind.XbtMipCompanion or RefKind.RtxMaterial or RefKind.MoveClip or RefKind.TextPath;

    /// <summary>Edge kinds whose target is a non-file id worth carrying flags for - its defining
    /// file (an `.xbm`, an `.spk` bank) becomes reachable through the definitions table.
    /// <see cref="RefKind.DepLoadDependency"/> is handled separately (doubly gated), and
    /// <see cref="RefKind.DepLoadTypeTag"/>/<see cref="RefKind.MgbStringResource"/> never
    /// propagate - one groups by anonymous type, the other names a localised string.</summary>
    public static bool PropagatesToName(RefKind kind) => kind is
        RefKind.FcbNameValue or RefKind.MgbNameId or RefKind.XbgMaterial or
        RefKind.SpkRecordLink or RefKind.SpkCategory or RefKind.SpkFlatCopySibling;

    /// <summary>
    /// Extensions whose plausible referrers the extractors cannot read (Havok rigs are picked by
    /// archetype logic never traced, pose/facial/ambience/bark ids come from formats without
    /// decoders, sound ids chain through partially-parsed banks, shaders outside the shadersobj
    /// family are keyed by permutation id). An unreached file of one of these types is
    /// <c>unknown</c>, never <c>unused</c> - absence of an edge is absence of a parser, not
    /// evidence of death.
    /// </summary>
    public static readonly IReadOnlySet<string> OpaqueReferrerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "hkx", "skeleton", "apm", "ambx", "lfe", "lfa", "pfe", "bank", "banklist", "mask", "raw",
        "bin", "bik", "mft", "root", "pub", "nomad", "console", "fx", "rs", "vso", "pso",
        "spk", "sbao", "bao",
    };

    /// <summary>
    /// Trees whose files are reached by runtime name composition the extractors cannot see:
    /// Domino graph and node paths are built from bare graph names ("domino\user" is a Dunia.dll
    /// prefix literal), so an unreached file here is <c>unknown</c> unless a curated rule says
    /// otherwise.
    /// </summary>
    public static readonly IReadOnlyList<string> OpaqueReferrerPrefixes = [@"domino\"];

    public static bool IsOpaquePath(string path)
        => OpaqueReferrerPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The one known CRC32 collision inside the shipped filelist (see
    /// docs/docs/modding/getting-started.md): one hash, two legitimate names. A hash-keyed verdict
    /// cannot tell which file it describes, so it is never allowed to say <c>unused</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, string> KnownCollisions = new Dictionary<uint, string>
    {
        [0x4A724578] = @"levels\ige_map\generated\sdat\sd10_shadow.xbt / scripts\game\barkdata\1436645.bank",
    };

    /// <summary>
    /// Console leftovers the PC build never selects, even though PC-reachable files reference them
    /// (the `.mgb.desc` icon_xenon/icon_ps3 prompt attributes point straight at the 360 textures).
    /// A deliberate reachability override, applied after the BFS: RE-verified platform knowledge
    /// beats a followed edge here.
    /// </summary>
    public static readonly IReadOnlyList<string> ConsoleOnlyPrefixes =
    [
        @"config\presets\ps3\",
        @"config\presets\xenon\",
        @"ui\textures\360\",
    ];

    public static bool IsConsoleOnly(string path)
        => ConsoleOnlyPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>An unused file naming this many other files reads as a manifest, not a leaf.</summary>
    public const int DecoyOutRefs = 20;

    /// <summary>An unused file this large feels load-bearing regardless of content.</summary>
    public const long DecoyBytes = 1 << 20;
}
