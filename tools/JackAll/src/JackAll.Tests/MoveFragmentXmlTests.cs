using JackAll.Core.Format.Move;

namespace JackAll.Tests;

/// <summary>
/// The fragment text: it must describe a state completely, and describe it without ever naming a
/// position in the file it was cut from.
/// </summary>
public sealed class MoveFragmentXmlTests
{
    public static TheoryData<string> CorpusFiles() => MoveStateIndexTests.CorpusFiles();

    /// <summary>
    /// Every state renders, parses back, and renders identically - the text is a fixed point, which
    /// is what <c>FragmentMerge</c> needs when it compares a vanilla ancestor against a mod's copy.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_state_renders_and_parses_back_to_the_same_text(string path)
    {
        if (path.Length == 0) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(File.ReadAllBytes(path)));

        int checked_ = 0;
        foreach (MoveObject state in index.TopLevelStates)
        {
            string once = MoveFragmentXml.Render(MoveFragmentXml.Lift(index, state));
            string twice = MoveFragmentXml.Render(MoveFragmentXml.Parse(once));
            Assert.Equal(once, twice);
            checked_++;
        }

        Assert.True(checked_ > 0);
    }

    /// <summary>
    /// A parsed fragment holds the same objects and the same ops as the state it came from, so
    /// nothing is silently dropped on the way through the text.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_parsed_fragment_holds_the_whole_subtree(string path)
    {
        if (path.Length == 0) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(File.ReadAllBytes(path)));

        foreach (MoveObject state in index.TopLevelStates)
        {
            MoveFragment lifted = MoveFragmentXml.Lift(index, state);
            MoveFragment parsed = MoveFragmentXml.Parse(MoveFragmentXml.Render(lifted));

            List<MoveObject> before = lifted.Objects();
            List<MoveObject> after = parsed.Objects();
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(lifted.StateHash, parsed.StateHash);
            Assert.Equal(lifted.External.Count, parsed.External.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].ClassName, after[i].ClassName);
                Assert.Equal(before[i].Ops.Count, after[i].Ops.Count);
            }
        }
    }

    /// <summary>
    /// The property that keeps fragments from churning: a fragment mentions its own local ids and
    /// state hashes, never an index into the file it was cut from.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_fragment_carries_no_whole_file_stream_indices(string path)
    {
        if (path.Length == 0) return;

        MoveFile file = MoveCodec.Load(File.ReadAllBytes(path));
        MoveStateIndex index = MoveStateIndex.Build(file);

        // The biggest state is the one most likely to reference outside itself.
        MoveObject biggest = index.TopLevelStates
            .MaxBy(s => MoveFragmentXml.Lift(index, s).Objects().Count)!;
        MoveFragment fragment = MoveFragmentXml.Lift(index, biggest);
        string xml = MoveFragmentXml.Render(fragment);

        // Local ids run 0..n-1 for this fragment alone, so none may reach the file's object count.
        int objects = fragment.Objects().Count;
        Assert.True(objects < file.Objects.Count);
        foreach (string line in xml.Split('\n').Where(l => l.Contains(" id=\"")))
        {
            int at = line.IndexOf(" id=\"", StringComparison.Ordinal) + 5;
            int end = line.IndexOf('"', at);
            Assert.True(int.Parse(line[at..end]) < objects, line.Trim());
        }
    }

    /// <summary>
    /// References that leave a state survive the text as addresses, and resolve back to the same
    /// object against the graph they came from.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void An_external_reference_survives_as_an_address(string path)
    {
        if (path.Length == 0) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(File.ReadAllBytes(path)));

        int external = 0;
        foreach (MoveObject state in index.TopLevelStates)
        {
            MoveFragment lifted = MoveFragmentXml.Lift(index, state);
            foreach (((MoveObject owner, int i), MoveAddress address) in lifted.External)
            {
                Assert.Same(owner.Ops[i].Target, index.Resolve(address));
                external++;
            }
        }

        Assert.True(external > 0, "the corpus is expected to reference across states");
    }
}
