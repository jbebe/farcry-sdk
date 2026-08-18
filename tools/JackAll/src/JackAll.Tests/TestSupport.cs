using System.Globalization;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Tests;

/// <summary>Shared setup helpers with no natural home in any one test class.</summary>
internal static class TestSupport
{
    /// <summary>The repo root, found by walking up from the test runner's output directory until a
    /// <c>tools\JackAll\assets</c> is in sight — same search-don't-assume approach as
    /// <see cref="LoadNames"/>, since the output path depends on configuration and TFM.</summary>
    public static string RepositoryRoot
    {
        get
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "tools", "JackAll", "assets")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return dir ?? AppContext.BaseDirectory;
        }
    }

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
    /// <see cref="FcbDocument.Serialize"/>/<see cref="FcbDocument.Deserialize"/> pair rather than
    /// <see cref="FcbDocument.EncodedSize"/> itself, since this is the independent oracle that method
    /// is checked against. 16 is the fixed "FCbn" file header's size (4-byte signature + 2-byte
    /// version + 2-byte flags + two 4-byte counts - see <see cref="FcbDocument"/>'s own remarks);
    /// <c>Serialize</c> always writes it once per call, so it has to be subtracted back out to get
    /// just <paramref name="obj"/>'s own bytes.</summary>
    public static long FullyExpandedFcbSize(FcbObject obj) => FcbDocument.Serialize(obj).Length - 16;

    /// <summary>Renders <paramref name="vanilla"/> with one value set on the node at
    /// <paramref name="childPath"/> (an index path from the fragment's own root; empty edits the root
    /// itself), leaving everything else byte-for-byte untouched — used by the merge tests
    /// (docs/design/fcb-fragment-overlays.md Milestone 3) to build two mods' edits that land in
    /// genuinely different regions of the rendered XML, or, aimed at the same existing value, a
    /// genuine collision.</summary>
    /// <summary>The id the deleted group-per-file export gave <paramref name="library"/>'s child at
    /// <paramref name="index"/> — the only id shape a mod staged before per-archetype ids can carry,
    /// and nothing in production defines it any more.</summary>
    public static string PreDeepGroupId(FcbObject library, int index)
    {
        string id = (index + 1).ToString(CultureInfo.InvariantCulture)
            .PadLeft(library.Children.Count.ToString(CultureInfo.InvariantCulture).Length, '0');
        if (library.Children[index].Values.TryGetValue(FcbClassDefinitions.Crc32Ascii("Name"), out byte[]? name)
            && name.Length > 1)
        {
            id += "_" + System.Text.Encoding.UTF8.GetString(name, 0, name.Length - 1);
        }
        return id + ".xml";
    }

    public static byte[] RenderWithValueSetAt(FcbObject vanilla, int[] childPath, uint valueHash, byte[] value)
    {
        string xml = FcbXml.ToXml(
            CloneWithValueSet(vanilla, childPath, 0, valueHash, value), FcbClassDefinitions.Empty);
        return System.Text.Encoding.UTF8.GetBytes(xml);
    }

    private static FcbObject CloneWithValueSet(FcbObject node, int[] path, int depth, uint valueHash, byte[] value)
    {
        var clone = new FcbObject { TypeHash = node.TypeHash };
        foreach ((uint hash, byte[] existing) in node.Values)
        {
            clone.Values[hash] = existing;
        }
        if (depth == path.Length)
        {
            clone.Values[valueHash] = value;
        }
        for (int i = 0; i < node.Children.Count; i++)
        {
            clone.Children.Add(depth < path.Length && path[depth] == i
                ? CloneWithValueSet(node.Children[i], path, depth + 1, valueHash, value)
                : node.Children[i]);
        }
        return clone;
    }

    /// <summary>Index paths to the first pair of sibling subtrees below <paramref name="fragment"/> —
    /// two edit targets whose rendered XML is far enough apart for diff3 to merge cleanly. Null when
    /// the tree never branches (a fragment too small to prove non-overlapping edits on).</summary>
    public static (int[] A, int[] B)? TwoDistantEditPaths(FcbObject fragment)
    {
        var prefix = new List<int>();
        FcbObject node = fragment;
        while (node.Children.Count < 2)
        {
            if (node.Children.Count == 0)
            {
                return null;
            }
            prefix.Add(0);
            node = node.Children[0];
        }
        return ([.. prefix, 0], [.. prefix, 1]);
    }

    /// <summary>The node at an index path produced by <see cref="TwoDistantEditPaths"/>.</summary>
    public static FcbObject NodeAt(FcbObject root, int[] childPath)
        => childPath.Aggregate(root, (node, index) => node.Children[index]);

    /// <summary>Depth-first search for the node carrying <paramref name="valueHash"/> — how a test
    /// relocates a spliced replacement that deliberately carries no identity of its own.</summary>
    public static FcbObject? FindNodeWithValue(FcbObject node, uint valueHash)
    {
        if (node.Values.ContainsKey(valueHash))
        {
            return node;
        }
        foreach (FcbObject child in node.Children)
        {
            if (FindNodeWithValue(child, valueHash) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Asserts two FCB trees carry the same type hashes, value keys, value bytes and child structure.
    /// <paramref name="assertSameValue"/> replaces the byte-for-byte value comparison, for a round trip
    /// that is deliberately lossy.
    /// </summary>
    public static void AssertSameShape(
        FcbObject expected, FcbObject actual, Action<byte[], byte[]>? assertSameValue = null)
    {
        assertSameValue ??= (e, a) => Assert.Equal(e, a);

        Assert.Equal(expected.TypeHash, actual.TypeHash);
        Assert.Equal(expected.Values.Keys.OrderBy(k => k), actual.Values.Keys.OrderBy(k => k));
        foreach (uint key in expected.Values.Keys)
        {
            assertSameValue(expected.Values[key], actual.Values[key]);
        }

        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertSameShape(expected.Children[i], actual.Children[i], assertSameValue);
        }
    }
}
