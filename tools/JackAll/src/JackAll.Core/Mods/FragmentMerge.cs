using System.Collections.Concurrent;
using System.Text;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// The Milestone 3 (docs/design/fcb-fragment-overlays.md) fragment-merge machinery shared by
/// <see cref="JackAll.Core.Vfs.GameVfs"/> and <see cref="PatchBuilder"/>. Neither the override index
/// nor the merge fold itself needs anything specific to either caller — only *obtaining* the vanilla
/// bytes to decode genuinely differs (<c>GameVfs.ReadOriginal</c> vs. <c>PatchBuilder</c>'s
/// <c>readArchiveOriginal</c> delegate), which is why this lives here rather than being folded into
/// one side or the other: <c>JackAll.Core.Mods</c> can't depend on <c>JackAll.Core.Vfs</c>, but
/// <c>GameVfs</c> already depends on <c>JackAll.Core.Mods</c>, so this is the one place both can reach.
/// </summary>
public static class FragmentMerge
{
    /// <summary>Container hash -&gt; fragment id -&gt; every enabled layer overriding it, in priority
    /// order (later in the list = higher priority, matching <paramref name="enabledLayers"/>' own
    /// order). Keyed via <see cref="FcbFragments.IdComparer"/>: a staged path is run through
    /// <c>NameHash.Normalize</c> (which lowercases it) on the way in, but the tree's own ids keep
    /// whatever case the game data's entity name actually has — and two mods spelling the same
    /// world-sector entity differently (its numeric id is what's authoritative) must land on one
    /// entry, not two competing ones.</summary>
    public static Dictionary<uint, Dictionary<string, List<(IModLayer Layer, uint EntryHash)>>> BuildOverrideIndex(
        IEnumerable<IModLayer> enabledLayers)
    {
        var overrides = new Dictionary<uint, Dictionary<string, List<(IModLayer, uint)>>>();
        foreach (IModLayer layer in enabledLayers)
        {
            foreach ((uint containerHash, IReadOnlyList<FragmentOverride> layerFragments) in layer.FragmentOverrides)
            {
                if (!overrides.TryGetValue(containerHash, out Dictionary<string, List<(IModLayer, uint)>>? byFragment))
                {
                    byFragment = new Dictionary<string, List<(IModLayer, uint)>>(FcbFragments.IdComparer);
                    overrides[containerHash] = byFragment;
                }
                foreach (FragmentOverride fo in layerFragments)
                {
                    if (!byFragment.TryGetValue(fo.FragmentId, out List<(IModLayer, uint)>? contributors))
                    {
                        contributors = [];
                        byFragment[fo.FragmentId] = contributors;
                    }
                    contributors.Add((layer, fo.EntryHash));
                }
            }
        }
        return overrides;
    }

    /// <summary>
    /// Reports every fragment of one container whose resolved overrides contradict another's - a
    /// deletion of something a sibling override also edits. <see cref="IContainerSplitter.Apply"/>
    /// keeps the entity either way; this is what stops that being a silent decision.
    /// </summary>
    /// <param name="conflicts">Null for the strict mode <see cref="JackAll.Core.Vfs.GameVfs"/> uses,
    /// which throws so the App can offer the row to hand-fix; a queue for the headless build, which
    /// records and carries on - the same split <see cref="Resolve"/> already makes.</param>
    public static void ReportContradictions(
        IContainerSplitter splitter, IReadOnlyDictionary<string, string> resolved,
        ConcurrentQueue<ModConflict>? conflicts, string container)
    {
        foreach ((string fragmentId, string kept, string overruled) in splitter.Contradictions(resolved))
        {
            if (conflicts is null)
            {
                throw new InvalidDataException(
                    $"For '{fragmentId}' in '{container}', {kept} wins over {overruled} - drop one of "
                    + "the two to say which you meant.");
            }

            conflicts.Enqueue(new ModConflict(container, kept, [overruled], FragmentId: fragmentId));
        }
    }

    /// <summary>
    /// The final XML for one fragment, folding every enabled layer touching it (in priority order)
    /// via a chain of 3-way merges (<see cref="IContainerSplitter.Merge"/>) against the vanilla
    /// ancestor. The first layer is taken outright, as its canonical text - what a fold against an
    /// unchanged "ours" means - so a fragment touched by exactly one layer is a byte-identical
    /// pass-through whatever the splitter's merge does.
    /// <paramref name="fragmentId"/> not matching anything in <paramref name="vanilla"/> is not an
    /// error: it means every contributing layer is adding a genuinely new entry rather than overriding
    /// an existing one (normal modding — see <see cref="IContainerSplitter.Apply"/>, which is what
    /// actually splices an added child in). A second layer contributing the same brand-new id then
    /// folds normally, against an empty ancestor, so what two mods adding the same id give differently
    /// is a real conflict, not one silently clobbering the other.
    /// </summary>
    /// <param name="conflicts">
    /// Null (the default) keeps the original behavior: a genuine collision throws
    /// <see cref="InvalidDataException"/>, which is right for <see cref="JackAll.Core.Vfs.GameVfs"/> -
    /// JackAll.App has an interactive row to hand-fix a conflict on, so silently picking a winner there
    /// would hide a real authoring decision from the person best placed to make it (and
    /// <c>GameVfsFragmentOverrideTests</c> pins exactly this throw).
    ///
    /// Non-null switches to "load order wins, but tell someone": the same rule whole-file overrides
    /// already follow (later layer replaces earlier, no questions asked) is applied to the fragment
    /// too, and one <see cref="ModConflict"/> is enqueued per collision instead of throwing. This
    /// is what <c>jackall-cli mod build</c> passes - a headless run driven by a mod manager (Vortex)
    /// has nobody to ask and no "Replace on that row" UI to point at, so refusing to build over a
    /// conflict it can't ask a human to resolve on the spot is worse than building with a flagged
    /// warning the mod manager can surface after the fact. <see cref="ConcurrentQueue{T}"/> rather than
    /// a plain list because <see cref="PatchBuilder"/> resolves every fragment across every container
    /// in one flat parallel pass - <c>Resolve</c> itself has to tolerate being called concurrently for
    /// different fragments sharing the same queue.
    /// </param>
    /// <param name="container">
    /// The container's display path, stamped onto every <see cref="ModConflict"/> this call
    /// enqueues - <paramref name="fragmentId"/> is relative to it and ambiguous on its own.
    /// </param>
    public static string Resolve(IContainerSplitter splitter, IContainerTree vanilla, string fragmentId,
        IReadOnlyList<(IModLayer Layer, uint EntryHash)> layers,
        ConcurrentQueue<ModConflict>? conflicts = null, string container = "")
    {
        string? vanillaXml = vanilla.Extract(fragmentId);
        bool isNewEntry = vanillaXml is null;
        string ancestor = vanillaXml ?? "";

        string result = ancestor;
        for (int i = 0; i < layers.Count; i++)
        {
            (IModLayer layer, uint entryHash) = layers[i];
            string theirs;
            try
            {
                theirs = splitter.Canonicalize(fragmentId, Encoding.UTF8.GetString(layer.Read(entryHash)));
            }
            catch (Exception ex) when (ex is not InvalidDataException)
            {
                // A splitter's parser throws a bare XmlException - "Data at the
                // root level is invalid. Line 1, position 1." for an empty file, and similarly opaque
                // messages for other malformed content - with no indication of which mod or file it
                // came from. This is user-supplied content (unlike ancestor/result below, which only
                // ever comes from the vanilla archive or a prior successful fold), so it's the one
                // spot in this loop actually worth naming a path for.
                string where = layer.PathOf(entryHash) ?? fragmentId;
                throw new InvalidDataException(
                    $"'{layer.Name}' has an unreadable fragment override at '{where}': {ex.Message} " +
                    "Check that file's contents in the mod - it's expected to be JackAll-exported " +
                    "fragment XML, not raw/binary data.", ex);
            }

            if (i == 0)
            {
                // "Ours" is the ancestor itself, so theirs wins outright.
                result = theirs;
                continue;
            }

            string ours = splitter.Canonicalize(fragmentId, result);
            (string merged, IReadOnlyList<string> at) = splitter.Merge(fragmentId, ancestor, ours, theirs);
            if (at.Count == 0)
            {
                result = merged;
                continue;
            }

            var conflict = new ModConflict(container, layer.Name,
                [.. layers.Take(i).Select(l => l.Layer.Name).Distinct()], FragmentId: fragmentId, IsNewEntry: isNewEntry)
            {
                Paths = [.. at.Where(path => path.Length > 0)],
            };
            if (conflicts is null)
            {
                throw new InvalidDataException(isNewEntry
                    ? $"'{layer.Name}' conflicts with another enabled mod, both adding a new entry " +
                      $"'{fragmentId}' with different content{conflict.AtPaths}. Hand-fix the fragment (Replace on " +
                      "that row) and re-stage it - your fix wins outright since the workspace is always highest priority."
                    : $"'{layer.Name}' conflicts with an earlier enabled mod inside '{fragmentId}'{conflict.AtPaths}. " +
                      "Hand-fix the fragment (Replace on that row) and re-stage it - your fix wins outright " +
                      "since the workspace is always highest priority.");
            }

            // Lenient mode: keep the splitter's own resolution of the collision. For a text fragment
            // that is the higher-priority layer outright, exactly like a whole-file override; for a
            // format that merges by meaning it is the fold with only the collision decided, so the
            // other layer's untouched edits survive.
            conflicts.Enqueue(conflict);
            result = merged;
        }
        return result;
    }
}
