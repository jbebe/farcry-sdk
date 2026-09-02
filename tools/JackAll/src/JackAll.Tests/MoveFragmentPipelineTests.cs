using System.Collections.Concurrent;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// A MOVE fragment as the mod pipeline sees it: staged at a path, merged by load order, and
/// colliding loudly when two mods edit one state.
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

    public static TheoryData<string> CorpusFiles() => MoveStateIndexTests.CorpusFiles();

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
    [MemberData(nameof(CorpusFiles))]
    public void Two_mods_editing_different_states_both_survive(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
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
    /// Two mods editing one state is a real conflict. A build resolves it by load order and reports
    /// it, so the losing edit is named rather than vanishing the way a whole-file override would.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Two_mods_editing_one_state_collide_loudly(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
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
        ConcurrentQueue<FragmentConflict> conflicts = new();
        string resolved = FragmentMerge.Resolve(
            MoveContainerSplitter.Instance, vanilla, id, contributors, conflicts, "movemgr.bin");

        Assert.Equal(theirs, resolved);
        FragmentConflict reported = Assert.Single(conflicts);
        Assert.Equal("Second", reported.WinningLayer);
        Assert.Equal(["First"], reported.EarlierLayers);
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
        const string marker = "<u32 n=\"m_animNameHash\" v=\"";
        foreach (FcbFragmentInfo row in tree.List())
        {
            string xml = tree.Extract(row.Id)!;
            int at = xml.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;

            int start = at + marker.Length;
            string clip = xml[start..xml.IndexOf('"', start)];
            if (xml.Split($"v=\"{clip}\"").Length != 2) continue;
            if (skip-- > 0) continue;

            return (row.Id, xml.Replace($"{marker}{clip}\" />", $"{marker}{replacement}\" />"));
        }

        throw new InvalidOperationException("not enough fragments hold a unique clip reference");
    }
}
