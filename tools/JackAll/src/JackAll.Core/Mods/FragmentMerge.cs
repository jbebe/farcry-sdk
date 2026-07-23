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
    /// order). Case-insensitive by fragment id: a staged path is run through <c>NameHash.Normalize</c>
    /// (which lowercases it) on the way in, but <c>FcbXml.ListFragmentIds</c> preserves whatever case
    /// the game data's entity name actually has, so an exact-case match would silently miss real
    /// matches.</summary>
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
                    byFragment = new Dictionary<string, List<(IModLayer, uint)>>(StringComparer.OrdinalIgnoreCase);
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
    /// The final XML for one fragment, folding every enabled layer touching it (in priority order)
    /// via a chain of 3-way merges against the vanilla ancestor. Starting <c>result</c> at the
    /// ancestor makes the first fold <c>Diff3.Merge(ancestor, ancestor, layer's text)</c>, which is a
    /// no-op pass-through for any input (see <see cref="Diff3"/>'s remarks) — so a fragment touched
    /// by exactly one layer behaves exactly as it did before Milestone 3, with no special-casing.
    /// <paramref name="fragmentId"/> not matching anything in <paramref name="vanillaRoot"/> isn't an
    /// error: it means every contributing layer is adding a genuinely new entry rather than overriding
    /// an existing one (normal modding — see <see cref="Format.Fcb.FcbAssembler.Apply"/>, which is what
    /// actually splices an added child in). There's no ancestor to fold the first contributor's content
    /// against in that case, so it's taken outright instead of going through <see cref="Diff3"/> at all
    /// — same byte-for-byte guarantee a single layer touching an existing fragment already gets, rather
    /// than relying on <see cref="Diff3"/>'s empty-ancestor behavior to happen to line up with it. A
    /// second layer contributing the same brand-new id then folds normally, against an empty ancestor,
    /// so different content from two mods adding the same id is a real conflict, not one silently
    /// clobbering the other.
    /// </summary>
    public static string Resolve(FcbObject vanillaRoot, string fragmentId,
        IReadOnlyList<(IModLayer Layer, uint EntryHash)> layers, FcbClassDefinitions defs)
    {
        string? vanillaXml = FcbXml.ExtractFragment(vanillaRoot, fragmentId, defs);
        bool isNewEntry = vanillaXml is null;
        string ancestor = vanillaXml ?? "";

        string result = ancestor;
        for (int i = 0; i < layers.Count; i++)
        {
            (IModLayer layer, uint entryHash) = layers[i];
            string theirs = FcbXml.CanonicalizeFragment(Encoding.UTF8.GetString(layer.Read(entryHash)), defs);

            if (isNewEntry && i == 0)
            {
                result = theirs;
                continue;
            }

            string ours = FcbXml.CanonicalizeFragment(result, defs);
            (result, bool conflict) = Diff3.Merge(ancestor, ours, theirs);
            if (conflict)
            {
                throw new InvalidDataException(isNewEntry
                    ? $"'{layer.Name}' conflicts with another enabled mod, both adding a new entry " +
                      $"'{fragmentId}' with different content. Hand-fix the fragment (Replace on that row) " +
                      "and re-stage it - your fix wins outright since the workspace is always highest priority."
                    : $"'{layer.Name}' conflicts with an earlier enabled mod inside '{fragmentId}'. " +
                      "Hand-fix the fragment (Replace on that row) and re-stage it - your fix wins outright " +
                      "since the workspace is always highest priority.");
            }
        }
        return result;
    }
}
