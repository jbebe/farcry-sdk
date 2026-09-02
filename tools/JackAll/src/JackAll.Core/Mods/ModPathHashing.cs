using System.Globalization;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// Maps files inside a mod to engine hashes.
/// </summary>
/// <remarks>
/// Two conventions, both needed:
///
///   worlds\world1\generated\x.fcb   → hashed as that relative path (the normal case)
///   _hash\4A724578.xbt              → used as the literal hash 0x4A724578
///
/// The second exists because the community's filename dictionary is incomplete. Without it, any
/// archive entry whose name nobody has recovered would be permanently unmoddable — you could see
/// the file but never override it, since you couldn't produce a path that hashes to it.
///
/// A third shape, layered on top of the first: a path with a segment (other than the last) ending in
/// <c>.fcb</c> overrides one fragment of a splitting `.fcb` — everything after that segment is the
/// fragment id, which may itself be nested (<c>FcbFragments</c>' path-shaped ids, e.g.
/// <c>entitylibrary.fcb\vehicle\land\jeep.xml</c>) — rather than being a standalone archive entry;
/// see <see cref="ModPathTarget.ContainerHash"/>. The same nameless-entry problem the <c>_hash\</c>
/// convention solves for a plain file applies one level deeper here too: an *unnamed* container's own
/// display path (<c>GameVfs.SyntheticPath</c>, e.g. <c>_unknown\data\1a2b3c4d.fcb</c>) is a display
/// convenience that deliberately doesn't hash back to the real archive hash, unlike a named
/// container's own recovered path — so overriding a fragment inside one needs the container's hash
/// spelled out directly: <c>_hash\1a2b3c4d.fcb\&lt;fragment id&gt;</c>.
/// </remarks>
internal static class ModPathHashing
{
    public const string HashFolder = "_hash";

    /// <summary>Reserved top-level folder holding a mod's FCSE plugin payload — synced into
    /// <c>bin\plugins</c> by <see cref="PluginSync"/>, never hashed into patch.dat.</summary>
    public const string PluginsFolder = "plugins";

    /// <summary>Reserved top-level folder a mod may wrap its game content in, stripped by
    /// <see cref="ContentPathOf"/> — so one archive can carry <c>mods\</c> and <c>plugins\</c>
    /// side by side.</summary>
    public const string ModsFolder = "mods";

    /// <summary>
    /// Vortex's own placeholder, dropped into any directory its deployment method would otherwise
    /// leave empty (hardlink/symlink deployment can't represent an empty folder, so it needs some
    /// file there to preserve it). It can land anywhere in a deployed mod's tree, including inside a
    /// fragment-override folder that has no real overrides staged in it (an empty container `.fcb\`) -
    /// treating it as content there means handing raw junk to <c>FcbXml.FromXml</c>. Not a mod file
    /// under any convention this class knows, so it's filtered before anything else runs.
    /// </summary>
    private const string VortexEmptyFolderMarker = "__folder_managed_by_vortex";

    /// <summary>
    /// Classifies one relative path the way every scan site must: a file under the reserved
    /// top-level <see cref="PluginsFolder"/> is plugin payload — never hashed, since its path would
    /// CRC to a junk archive entry (a hypothetical real entry named <c>plugins\…</c> stays
    /// overridable via <c>_hash\</c>) — a file under the reserved top-level <see cref="ModsFolder"/>
    /// resolves as content with that wrapper stripped, and everything else is ignored: the two
    /// folders are the whole layer contract, with no root-layout fallback. One entry point, one
    /// normalization pass, so the sites can't compose the rules in different orders.
    /// </summary>
    public static LayerPath Classify(string relativePath)
    {
        string normalized = NameHash.Normalize(relativePath);
        if (normalized.StartsWith(PluginsFolder + "\\", StringComparison.Ordinal))
        {
            string sub = normalized[(PluginsFolder.Length + 1)..];
            bool ignored = sub.Length == 0 || IsVortexMarker(sub);
            return new LayerPath(ignored ? null : sub, normalized, null);
        }

        if (normalized.StartsWith(ModsFolder + "\\", StringComparison.Ordinal))
        {
            string content = normalized[(ModsFolder.Length + 1)..];
            return new LayerPath(null, content, ResolveNormalized(content));
        }

        return new LayerPath(null, normalized, null);
    }

    /// <summary>The archive-relative content path: normalized, with the <see cref="ModsFolder"/>
    /// wrapper stripped. Only meaningful for a path <see cref="Classify"/> accepted as content.</summary>
    public static string ContentPathOf(string relativePath)
    {
        string normalized = NameHash.Normalize(relativePath);
        return normalized.StartsWith(ModsFolder + "\\", StringComparison.Ordinal)
            ? normalized[(ModsFolder.Length + 1)..]
            : normalized;
    }

    /// <summary>Whether the path's leaf is Vortex's placeholder (see
    /// <see cref="VortexEmptyFolderMarker"/>).</summary>
    private static bool IsVortexMarker(string normalizedPath)
        => normalizedPath.AsSpan(normalizedPath.LastIndexOf('\\') + 1)
            .Equals(VortexEmptyFolderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a relative path to what it overrides. Null for paths that are not overrides
    /// at all (readme files, Vortex's own deployment bookkeeping, and the like).</summary>
    public static ModPathTarget? Resolve(string relativePath)
        => ResolveNormalized(NameHash.Normalize(relativePath));

    private static ModPathTarget? ResolveNormalized(string normalized)
    {
        if (normalized.Length == 0 || IsVortexMarker(normalized))
        {
            return null;
        }

        string[] segments = normalized.Split('\\');

        if (segments[0] == HashFolder)
        {
            return ResolveHashAddressed(normalized, segments);
        }

        // A named container's fragment: some segment before the last ends in .fcb.
        if (ContainerPathOf(segments) is { } containerPath)
        {
            string fragmentId = normalized[(containerPath.Length + 1)..];
            GuardNotGroupId(fragmentId, normalized);
            return new ModPathTarget(
                NameHash.Compute(normalized), NameHash.Compute(containerPath), fragmentId);
        }

        return new ModPathTarget(NameHash.Compute(normalized), null, null);
    }

    /// <summary>The container part of a normalized fragment path - everything up to and including the
    /// first non-final segment ending in <c>.fcb</c> - or null when the path names no fragment. The
    /// one definition of where a container ends and a fragment id begins.</summary>
    internal static string? ContainerPathOf(string normalizedPath)
        => ContainerPathOf(normalizedPath.Split('\\'));

    private static string? ContainerPathOf(string[] segments)
    {
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (IsContainerSegment(segments[i]))
            {
                return string.Join('\\', segments[..(i + 1)]);
            }
        }
        return null;
    }

    /// <summary>Whether this path segment is a splitting container's own name.</summary>
    internal static bool IsContainerSegment(string segment)
        => ContainerFormats.IsContainerSegment(segment);

    /// <summary>
    /// Rejects the removed group-per-file id space (<c>NN_Name.xml</c> directly inside a container
    /// folder). Nothing produces those any more, and one staged by an older JackAll names no fragment
    /// — it would be appended as a phantom group instead of overriding the archetypes it came from, so
    /// it fails loudly rather than half-applying. Only at a container's own root: deeper in, a segment
    /// that happens to look like one is just part of an archetype's path.
    /// </summary>
    private static void GuardNotGroupId(string fragmentId, string normalized)
    {
        int underscore = fragmentId.IndexOf('_');
        if (underscore <= 0
            || fragmentId.Contains('\\')
            || !fragmentId.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || fragmentId.AsSpan(0, underscore).ContainsAnyExceptInRange('0', '9'))
        {
            return;
        }

        // A placed entity's id ends in its authoritative numeric id, which Canonicalize strips - so a
        // shorter canonical form means this is an entity ("12_Crate.2058514756624450165.xml"), not a
        // group. Only an id with nothing numeric to strip is the group shape.
        if (FcbFragments.Canonicalize(fragmentId).Length != fragmentId.Length)
        {
            return;
        }

        throw new InvalidDataException(
            $"'{normalized}' uses the removed NN_Name.xml group id space, which names no fragment - " +
            "staging it would append a phantom group rather than override anything. Re-export and " +
            "stage the archetype you meant to change.");
    }

    /// <summary>
    /// <c>_hash\&lt;hex&gt;[.ext]</c> (a plain unnamed override), or <c>_hash\&lt;hex&gt;.fcb\&lt;fragment id&gt;</c>
    /// (a fragment override inside an unnamed container — the hex is the *container's* hash, read
    /// straight off this segment rather than computed from any path, since none exists for it).
    /// </summary>
    private static ModPathTarget? ResolveHashAddressed(string normalized, string[] segments)
    {
        if (segments.Length < 2)
        {
            return null;
        }

        string leaf = segments[1];
        // Everything before the first dot is the hash: "4a724578.xbt" and a bare "4a724578" both
        // work, so a user can drop in a file with or without an extension.
        int dot = leaf.IndexOf('.');
        string hexPart = dot < 0 ? leaf : leaf[..dot];
        if (!uint.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
        {
            return null;
        }

        if (segments.Length == 2)
        {
            return new ModPathTarget(hash, null, null);
        }

        if (!IsContainerSegment(leaf))
        {
            return null; // extra segments after anything but a container leaf mean nothing
        }

        string fragmentId = string.Join('\\', segments[2..]);
        GuardNotGroupId(fragmentId, normalized);
        return new ModPathTarget(NameHash.Compute(normalized), hash, fragmentId);
    }

    /// <summary>
    /// Indexes one resolved target into a layer's <c>Hashes</c>/<c>FragmentOverrides</c> bookkeeping —
    /// shared so <see cref="ZipModLayer"/>'s constructor and <see cref="FolderModLayer"/>'s
    /// <c>Rescan</c>/<c>Stage</c> classify identically. A folder layer's <c>Stage</c> (which must
    /// update this immediately, not just on the next <c>Rescan</c> — callers build a patch right
    /// after staging with no rescan in between) can call this unconditionally: the per-container map
    /// is keyed by <see cref="FcbFragments.IdComparer"/>, so re-adding a fragment replaces its prior
    /// entry even under a different spelling of the same id (an entity's name prefix is cosmetic),
    /// which would otherwise leave one layer holding two overrides of one fragment and conflicting
    /// with itself.
    /// </summary>
    public static void Add(
        ModPathTarget target, HashSet<uint> hashes, Dictionary<uint, FragmentMap> fragmentOverrides)
    {
        if (target.ContainerHash is not { } containerHash)
        {
            hashes.Add(target.EntryHash);
            return;
        }

        if (!fragmentOverrides.TryGetValue(containerHash, out FragmentMap? fragments))
        {
            fragments = new FragmentMap();
            fragmentOverrides[containerHash] = fragments;
        }
        fragments[target.FragmentId!] = new FragmentOverride(target.FragmentId!, target.EntryHash);
    }

    /// <summary>Snapshots the mutable per-container fragment maps into the immutable shape
    /// <see cref="IModLayer.FragmentOverrides"/> exposes.</summary>
    public static IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> Freeze(
        Dictionary<uint, FragmentMap> fragmentOverrides)
        => fragmentOverrides.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<FragmentOverride>)[.. kv.Value.Values]);
}

/// <summary>What one relative path inside a mod resolves to — see <see cref="ModPathHashing.Resolve"/>.</summary>
internal readonly record struct ModPathTarget(uint EntryHash, uint? ContainerHash, string? FragmentId);

/// <summary>One path's role in a layer (see <see cref="ModPathHashing.Classify"/>): a plugin payload
/// file (<see cref="PluginPath"/> non-null), a content override (<see cref="Target"/> non-null), or
/// neither — an ignored file. <see cref="ContentPath"/> is the normalized path the target was
/// resolved from, mods\ wrapper already stripped.</summary>
internal readonly record struct LayerPath(string? PluginPath, string ContentPath, ModPathTarget? Target);

/// <summary>One container's overridden fragments, keyed so two spellings of one id can't both land.</summary>
internal sealed class FragmentMap() : Dictionary<string, FragmentOverride>(FcbFragments.IdComparer);
