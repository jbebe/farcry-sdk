using System.Collections.Concurrent;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// A MOVE fragment as the mod pipeline sees it: staged at a path, merged op by op, and colliding
/// loudly when two mods edit one clip.
/// </summary>
/// <remarks>
/// This is the point of the whole exercise. Before it, a mod that retargets one animation clip ships
/// the entire 1.8 MB graph as a whole-file override, and whole-file overrides are last-wins and
/// silent - so two mods that each touch an animation cannot coexist and neither is told.
/// </remarks>
public sealed class MoveFragmentPipelineTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("jackall-move-fragments").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_staged_move_fragment_classifies_against_its_container()
    {
        string layer = Path.Combine(_root, "MyMod");
        string staged = Path.Combine(
            layer, "mods", "graphics", "move", "movemgr.bin", "pawn_aim.1746764574.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "<MoveState state=\"1746764574\" class=\"CMoveState\" />");

        FolderModLayer read = new(layer, "MyMod");

        Assert.Empty(read.Hashes);
        (uint container, IReadOnlyList<FragmentOverride> fragments) = Assert.Single(read.FragmentOverrides);
        Assert.Equal(JackAll.Core.Format.NameHash.Compute(@"graphics\move\movemgr.bin"), container);
        Assert.Equal("pawn_aim.1746764574.xml", Assert.Single(fragments).FragmentId);
    }

    /// <summary>
    /// The property that makes this worth building: disjoint edits never meet, so two animation mods
    /// compose without either noticing.
    /// </summary>
    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Two_mods_editing_different_states_both_survive(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree vanilla = MoveContainerSplitter.Instance.Open(original);
        (string firstId, string firstEdit) = EditAClip(vanilla, 0, 0x0BADC0DE);
        (string secondId, string secondEdit) = EditAClip(vanilla, 1, 0x0DEFACED);
        Assert.NotEqual(firstId, secondId);

        byte[] built = MoveContainerSplitter.Instance.Apply(original, new Dictionary<string, string>
        {
            [firstId] = firstEdit,
            [secondId] = secondEdit,
        });

        IContainerTree after = MoveContainerSplitter.Instance.Open(built);
        Assert.Equal(firstEdit, after.Extract(firstId));
        Assert.Equal(secondEdit, after.Extract(secondId));
    }

    /// <summary>
    /// Two mods retargeting one clip is a real conflict. A build resolves it by load order and reports
    /// it, so the losing edit is named rather than vanishing the way a whole-file override would.
    /// </summary>
    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Two_mods_editing_one_clip_collide_loudly(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree vanilla = MoveContainerSplitter.Instance.Open(original);
        (string id, string mine) = EditAClip(vanilla, 0, 0x0BADC0DE);
        (string _, string theirs) = EditAClip(vanilla, 0, 0x0DEFACED);

        IModLayer first = Layer("First", path, id, mine);
        IModLayer second = Layer("Second", path, id, theirs);
        List<(IModLayer, uint)> contributors =
        [
            (first, first.FragmentOverrides.Values.Single()[0].EntryHash),
            (second, second.FragmentOverrides.Values.Single()[0].EntryHash),
        ];

        // GameVfs refuses, because the app has an interactive row to hand-fix the conflict on.
        Assert.Throws<InvalidDataException>(() => FragmentMerge.Resolve(
            MoveContainerSplitter.Instance, vanilla, id, contributors));

        // A headless build takes the later layer and records that it did.
        ConcurrentQueue<ModConflict> conflicts = new();
        string resolved = FragmentMerge.Resolve(
            MoveContainerSplitter.Instance, vanilla, id, contributors, conflicts, "movemgr.bin");

        Assert.Equal(theirs, resolved);
        ModConflict reported = Assert.Single(conflicts);
        Assert.Equal("Second", reported.WinningLayer);
        Assert.Equal(["First"], reported.OverruledLayers);
    }

    /// <summary>A state merges op by op, so two mods retargeting different clips in it both land.</summary>
    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Two_mods_editing_different_clips_of_one_state_both_land(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree vanilla = MoveContainerSplitter.Instance.Open(original);
        (string id, string xml) = vanilla.List().Select(row => (row.Id, Xml: vanilla.Extract(row.Id)!))
            .First(fragment => UniqueClips(fragment.Xml).Count >= 2);
        (string first, string second) = (UniqueClips(xml)[0], UniqueClips(xml)[1]);

        (string resolved, ConcurrentQueue<ModConflict> conflicts) = Resolve(original, path, id,
            Retarget(xml, first, 0x0BADC0DE), Retarget(xml, second, 0x0DEFACED));

        Assert.Empty(conflicts);
        Assert.Equal(Canonical(id, Retarget(Retarget(xml, first, 0x0BADC0DE), second, 0x0DEFACED)), resolved);
    }

    /// <summary>
    /// Object ids are positions and a reference names one by id, so a mod that removes an object
    /// renumbers everything after it. Merged with another mod's clip edit, every reference still
    /// lands on the object it named.
    /// </summary>
    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void A_state_one_mod_reshapes_keeps_its_references_through_a_merge(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree vanilla = MoveContainerSplitter.Instance.Open(original);
        (string id, string xml, List<int> removable) = Referencing(vanilla);
        string clip = UniqueClips(xml)[0];
        string ours = Canonical(id, Nulled(xml, removable[0]));

        (string resolved, ConcurrentQueue<ModConflict> conflicts) = Resolve(original, path, id,
            ours, Retarget(xml, clip, 0x0BADC0DE));

        Assert.Empty(conflicts);
        Assert.Equal(Canonical(id, Retarget(ours, clip, 0x0BADC0DE)), resolved);
    }

    /// <summary>Two mods both reshaping a state that holds references could leave one naming the
    /// wrong object, so the higher-priority mod's state is kept whole and the collision reported.</summary>
    [Theory]
    [MemberData(nameof(MoveStateIndexTests.Graphs), MemberType = typeof(MoveStateIndexTests))]
    public void Two_mods_reshaping_a_state_with_references_keep_theirs_whole(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        IContainerTree vanilla = MoveContainerSplitter.Instance.Open(original);
        (string id, string xml, List<int> removable) = Referencing(vanilla);
        string theirs = Canonical(id, Nulled(xml, removable[^1]));

        (string resolved, ConcurrentQueue<ModConflict> conflicts) = Resolve(original, path, id,
            Canonical(id, Nulled(xml, removable[0])), theirs);

        Assert.Equal(theirs, resolved);
        Assert.Empty(Assert.Single(conflicts).Paths);
    }

    private (string Resolved, ConcurrentQueue<ModConflict> Conflicts) Resolve(
        byte[] original, string path, string id, string mine, string theirs)
    {
        ConcurrentQueue<ModConflict> conflicts = new();
        Dictionary<string, string> resolved = TestSupport.ResolveFragments(
            MoveContainerSplitter.Instance, original, $@"graphics\move\{Path.GetFileName(path)}", conflicts,
            Layer("First", path, id, mine), Layer("Second", path, id, theirs));
        return (Assert.Single(resolved).Value, conflicts);
    }

    private static string Canonical(string id, string xml) => MoveContainerSplitter.Instance.Canonicalize(id, xml);

    private const string ClipMarker = "<u32 n=\"m_animNameHash\" v=\"";

    /// <summary>Clip hashes a fragment names exactly once.</summary>
    private static List<string> UniqueClips(string xml)
        => [.. xml.Split(ClipMarker).Skip(1).Select(rest => rest[..rest.IndexOf('"')])
            .Where(clip => xml.Split($"v=\"{clip}\"").Length == 2)];

    private static string Retarget(string xml, string clip, uint replacement)
        => xml.Replace($"{ClipMarker}{clip}\" />", $"{ClipMarker}{replacement}\" />");

    /// <summary>A state holding references, and the positions of its objects a mod can null out ahead
    /// of a referenced one (no target or clip inside); at least two that do not nest.</summary>
    private static (string Id, string Xml, List<int> Removable) Referencing(IContainerTree tree)
    {
        foreach (FcbFragmentInfo row in tree.List())
        {
            string xml = tree.Extract(row.Id)!;
            XElement root = XElement.Parse(xml);
            HashSet<string> targets = [.. root.Descendants("ref").Select(r => (string)r.Attribute("id")!)];
            if (targets.Count == 0 || UniqueClips(xml).Count == 0)
            {
                continue;
            }

            int last = targets.Max(int.Parse);
            List<XElement> objects = [.. root.Descendants("obj")];
            List<int> removable = [.. objects.Index()
                .Where(o => !o.Item.DescendantsAndSelf("obj").Any(d => targets.Contains((string)d.Attribute("id")!))
                    && !o.Item.Descendants().Any(d => (string?)d.Attribute("n") == "m_animNameHash")
                    && o.Item.DescendantsAndSelf("obj").Max(d => int.Parse((string)d.Attribute("id")!)) < last)
                .Select(o => o.Index)];
            if (removable.Count >= 2 && !objects[removable[0]].DescendantsAndSelf().Contains(objects[removable[^1]]))
            {
                return (row.Id, xml, removable);
            }
        }

        throw new InvalidOperationException("no state holds references and two objects to remove ahead of them");
    }

    /// <summary>The fragment with its <paramref name="index"/>-th object replaced by a null pointer.</summary>
    private static string Nulled(string xml, int index)
    {
        XElement root = XElement.Parse(xml);
        XElement obj = root.Descendants("obj").ElementAt(index);
        obj.ReplaceWith(new XElement("null", new XAttribute("n", (string)obj.Attribute("n")!)));
        return root.ToString();
    }

    private IModLayer Layer(string name, string containerPath, string fragmentId, string xml)
    {
        string layer = Path.Combine(_root, name);
        string staged = Path.Combine(
            layer, "mods", "graphics", "move", Path.GetFileName(containerPath), fragmentId);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, xml);
        return new FolderModLayer(layer, name);
    }

    /// <summary>Rewrites one uniquely-valued clip reference in the <paramref name="skip"/>-th
    /// fragment that has one.</summary>
    private static (string Id, string Xml) EditAClip(IContainerTree tree, int skip, uint replacement)
    {
        foreach (FcbFragmentInfo row in tree.List())
        {
            string xml = tree.Extract(row.Id)!;
            int at = xml.IndexOf(ClipMarker, StringComparison.Ordinal);
            if (at < 0) continue;

            int start = at + ClipMarker.Length;
            string clip = xml[start..xml.IndexOf('"', start)];
            if (xml.Split($"v=\"{clip}\"").Length != 2) continue;
            if (skip-- > 0) continue;

            return (row.Id, Retarget(xml, clip, replacement));
        }

        throw new InvalidOperationException("not enough fragments hold a unique clip reference");
    }
}
