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
