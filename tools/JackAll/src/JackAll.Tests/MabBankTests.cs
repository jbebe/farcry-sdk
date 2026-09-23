using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// A whole bank rebuilt from its decoded clips: the chain nested back together and the tag table
/// repointed at where each clip landed.
/// </summary>
/// <remarks>
/// This is the piece a clip writer needs and the one that fails quietly. A chain is nested, so
/// changing any clip's size moves every clip after it, and each tag record carries its clip as a
/// delta from the record's own position - get one wrong and the animation misbehaves without
/// crashing, the same failure mode as an unsorted depload.
/// <para>
/// The banks rebuilt from decoded clips carry no event chunk: it is FCB, its length is not
/// computable from anything decoded, and carrying it verbatim would carry its padding too.
/// </para>
/// </remarks>
public sealed class MabBankTests
{
    [Theory]
    [InlineData(MabFixtures.Pistol, true)]
    [InlineData(MabFixtures.Tie, false)]
    public void A_bank_rebuilds_with_its_chain_and_tags_intact(string fixture, bool byteExact)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        MabFile bank = MabFile.Parse(original);
        List<MabClipParts> parts = [.. bank.Clips().Select(clip => MabClipParts.Of(clip, MabSections.Intrinsic(clip)))];
        byte[] produced = MabEncoder.AssembleBank(bank.Header, parts);

        Assert.Equal(original.Length, produced.Length);
        Assert.True(SameFraming(original, produced), $"{fixture}: a clip or tag moved.");
        if (byteExact)
        {
            Fixture.AssertSameBytes(fixture, original, produced);
        }
    }

    /// <summary>
    /// A bank re-laid from its sections' own bytes has to come back exactly.
    /// </summary>
    /// <remarks>
    /// This is what makes rewriting one clip safe. A bank holds the character's motion as well as
    /// the weapon's, and re-encoding the lot loses bytes wherever a rotation ties - so a writer that
    /// rebuilt everything would perturb clips nobody touched. Carrying an untouched clip's sections
    /// verbatim instead lands them exactly where they were, and the only clip re-encoded is the one
    /// somebody edited.
    /// <para>
    /// It only works at a section's *intrinsic* length: the block a reader slices runs to wherever
    /// the next section starts, so it carries the alignment padding and the separator with it, and
    /// re-laying those adds them a second time - one separator per clip.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(MabFixtures.Reload)]
    [InlineData(MabFixtures.Tie)]
    public void A_bank_relaid_from_its_own_section_bytes_is_unchanged(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        MabFile bank = MabFile.Parse(original);
        List<MabClipParts> parts = [.. bank.Clips().Select(clip => MabClipParts.Of(clip, Verbatim(clip)))];
        byte[] produced = MabEncoder.AssembleBank(bank.Header, parts);

        Fixture.AssertSameBytes(fixture, original, produced);
    }

    /// <summary>Every section a clip carries, at its own length.</summary>
    private static Dictionary<int, byte[]> Verbatim(MabClip clip)
    {
        Dictionary<int, byte[]> sections = [];
        for (int slot = 0; slot < MabClip.SectionCount; slot++)
        {
            if (slot == MabClip.SectionNextClip)
            {
                continue;
            }
            if (clip.IntrinsicSection(slot) is { } bytes)
            {
                sections[slot] = bytes;
            }
            else if (clip.Sections[slot] != 0)
            {
                // A slot that names an empty section still has to be named back.
                sections[slot] = [];
            }
        }
        return sections;
    }

    /// <summary>
    /// Whether every clip in both chains sits at the same offset and names the same sections - which
    /// is what the tag deltas and the nesting have to get right, independent of the bytes inside.
    /// </summary>
    private static bool SameFraming(byte[] original, byte[] produced)
    {
        List<MabClip> before = MabFile.Parse(original).Clips();
        List<MabClip> after = MabFile.Parse(produced).Clips();
        if (before.Count != after.Count)
        {
            return false;
        }

        for (int index = 0; index < before.Count; index++)
        {
            if (!before[index].Sections.SequenceEqual(after[index].Sections))
            {
                return false;
            }
        }

        // Every tag record has to reach the clip it names, which is what the deltas encode.
        List<MabParticipant> expected = MabFile.Parse(original).Participants();
        List<MabParticipant> got = MabFile.Parse(produced).Participants();
        return expected.Count == got.Count
            && expected.Zip(got).All(pair => pair.First.ClipOffset == pair.Second.ClipOffset);
    }
}
