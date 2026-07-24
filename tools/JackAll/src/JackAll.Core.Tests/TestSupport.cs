using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core.Tests;

/// <summary>Shared setup helpers with no natural home in any one test class.</summary>
internal static class TestSupport
{
    /// <summary>Walks up from the test runner's own output directory (e.g. bin\Debug\net10.0) to
    /// find the repo's checked-in <c>assets\fc2.hashlist</c> — it only ever lives under
    /// JackAll.App's output, not this project's own, so every caller needs to search for it rather
    /// than assuming a fixed relative path.</summary>
    public static NameDatabase LoadNames()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "assets", "fc2.hashlist")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }
        return NameDatabase.Load(Path.Combine(dir!, "assets", "fc2.hashlist"));
    }

    /// <summary>What <paramref name="obj"/> would serialize to on its own, fully expanded (no
    /// backreference dedup - <see cref="FcbDocument.Serialize"/> never emits it). Reuses the public
    /// <see cref="FcbDocument.Serialize"/>/<see cref="FcbDocument.Deserialize"/> pair rather than a
    /// dedicated size-computing method in <see cref="FcbDocument"/> itself, since this is only ever
    /// needed as a test oracle - production code gets a fragment's real on-disk size straight off
    /// <see cref="FcbDocument.DeserializeWithChildSizes"/> instead, which this exists to check against.
    /// 16 is the fixed "FCbn" file header's size (4-byte signature + 2-byte version + 2-byte flags +
    /// two 4-byte counts - see <see cref="FcbDocument"/>'s own remarks); <c>Serialize</c> always writes
    /// it once per call, so it has to be subtracted back out to get just <paramref name="obj"/>'s own
    /// bytes.</summary>
    public static long FullyExpandedFcbSize(FcbObject obj) => FcbDocument.Serialize(obj).Length - 16;

    /// <summary>Sets one value on the child at <paramref name="childIndex"/>, leaving every other
    /// child (and the fragment's own top-level values) byte-for-byte untouched — used by the
    /// Milestone 3 merge tests (docs/design/fcb-fragment-overlays.md) to build two mods' edits that
    /// land in genuinely different, non-adjacent regions of the rendered XML (different children
    /// render as widely separated <c>&lt;object&gt;</c> blocks), unlike two edits both appended at the
    /// same tail position, which even diff3 correctly treats as a real conflict.</summary>
    public static byte[] RenderWithChildValueSet(FcbObject vanilla, int childIndex, uint valueHash, byte[] value)
    {
        var edited = new FcbObject { TypeHash = vanilla.TypeHash };
        foreach ((uint hash, byte[] existing) in vanilla.Values)
        {
            edited.Values[hash] = existing;
        }
        for (int i = 0; i < vanilla.Children.Count; i++)
        {
            FcbObject child = vanilla.Children[i];
            if (i != childIndex)
            {
                edited.Children.Add(child);
                continue;
            }

            var editedChild = new FcbObject { TypeHash = child.TypeHash };
            foreach ((uint hash, byte[] existing) in child.Values)
            {
                editedChild.Values[hash] = existing;
            }
            editedChild.Values[valueHash] = value;
            foreach (FcbObject grandchild in child.Children)
            {
                editedChild.Children.Add(grandchild);
            }
            edited.Children.Add(editedChild);
        }
        string xml = FcbXml.ToXml(edited, FcbClassDefinitions.Empty).IndexXml;
        return System.Text.Encoding.UTF8.GetBytes(xml);
    }
}
