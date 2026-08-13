using System.Globalization;
using JackAll.Core.Format;

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
/// <c>.fcb</c> overrides one fragment of a splitting `.fcb` (see <c>FcbXml.ListFragmentIds</c>'s
/// <c>NN_Name.xml</c> naming) rather than being a standalone archive entry — see
/// <see cref="ModPathTarget.ContainerHash"/>. The same nameless-entry problem the <c>_hash\</c>
/// convention solves for a plain file applies one level deeper here too: an *unnamed* container's own
/// display path (<c>GameVfs.SyntheticPath</c>, e.g. <c>_unknown\data\1a2b3c4d.fcb</c>) is a display
/// convenience that deliberately doesn't hash back to the real archive hash, unlike a named
/// container's own recovered path — so overriding a fragment inside one needs the container's hash
/// spelled out directly: <c>_hash\1a2b3c4d.fcb\03_Foo.xml</c>.
/// </remarks>
internal static class ModPathHashing
{
    public const string HashFolder = "_hash";

    /// <summary>
    /// Vortex's own placeholder, dropped into any directory its deployment method would otherwise
    /// leave empty (hardlink/symlink deployment can't represent an empty folder, so it needs some
    /// file there to preserve it). It can land anywhere in a deployed mod's tree, including inside a
    /// fragment-override folder that has no real overrides staged in it (an empty `NN_Name.fcb\`) -
    /// treating it as content there means handing raw junk to <c>FcbXml.FromXml</c>. Not a mod file
    /// under any convention this class knows, so it's filtered before anything else runs.
    /// </summary>
    private const string VortexEmptyFolderMarker = "__folder_managed_by_vortex";

    /// <summary>Resolves a relative path to what it overrides. Null for paths that are not overrides
    /// at all (readme files, Vortex's own deployment bookkeeping, and the like).</summary>
    public static ModPathTarget? Resolve(string relativePath)
    {
        string normalized = NameHash.Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return null;
        }

        string[] segments = normalized.Split('\\');

        if (segments[^1].Equals(VortexEmptyFolderMarker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (segments[0] == HashFolder)
        {
            return ResolveHashAddressed(normalized, segments);
        }

        // A named container's fragment: some segment before the last ends in .fcb.
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (!segments[i].EndsWith(".fcb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string containerPath = string.Join('\\', segments[..(i + 1)]);
            string fragmentId = string.Join('\\', segments[(i + 1)..]);
            return new ModPathTarget(
                NameHash.Compute(normalized), NameHash.Compute(containerPath), fragmentId);
        }

        return new ModPathTarget(NameHash.Compute(normalized), null, null);
    }

    /// <summary>
    /// <c>_hash\&lt;hex&gt;[.ext]</c> (a plain unnamed override), or <c>_hash\&lt;hex&gt;.fcb\NN_Name.xml</c>
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

        string extension = dot < 0 ? "" : leaf[dot..];
        if (!extension.Equals(".fcb", StringComparison.OrdinalIgnoreCase))
        {
            return null; // extra segments after anything but a _hash\<hex>.fcb leaf mean nothing
        }

        string fragmentId = string.Join('\\', segments[2..]);
        return new ModPathTarget(NameHash.Compute(normalized), hash, fragmentId);
    }

    /// <summary>
    /// Indexes one resolved target into a layer's <c>Hashes</c>/<c>FragmentOverrides</c> bookkeeping —
    /// shared so <see cref="ZipModLayer"/>'s constructor and <see cref="FolderModLayer"/>'s
    /// <c>Rescan</c>/<c>Stage</c> classify identically. Idempotent: re-adding the same fragment
    /// replaces its prior entry rather than duplicating it, so a folder layer's <c>Stage</c> (which
    /// must update this immediately, not just on the next <c>Rescan</c> — callers build a patch right
    /// after staging with no rescan in between) can call this unconditionally.
    /// </summary>
    public static void Add(
        ModPathTarget target, HashSet<uint> hashes, Dictionary<uint, List<FragmentOverride>> fragmentOverrides)
    {
        if (target.ContainerHash is not { } containerHash)
        {
            hashes.Add(target.EntryHash);
            return;
        }

        if (!fragmentOverrides.TryGetValue(containerHash, out List<FragmentOverride>? fragments))
        {
            fragments = [];
            fragmentOverrides[containerHash] = fragments;
        }
        fragments.RemoveAll(f => f.EntryHash == target.EntryHash);
        fragments.Add(new FragmentOverride(target.FragmentId!, target.EntryHash));
    }

    /// <summary>Snapshots the mutable per-container fragment lists into the immutable shape
    /// <see cref="IModLayer.FragmentOverrides"/> exposes.</summary>
    public static IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> Freeze(
        Dictionary<uint, List<FragmentOverride>> fragmentOverrides)
        => fragmentOverrides.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<FragmentOverride>)kv.Value);
}

/// <summary>What one relative path inside a mod resolves to — see <see cref="ModPathHashing.Resolve"/>.</summary>
internal readonly record struct ModPathTarget(uint EntryHash, uint? ContainerHash, string? FragmentId);
