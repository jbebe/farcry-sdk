using System.Text.Json;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// A bank decoded to the format-free document a <c>.fc2model</c> carries, built back, and required
/// to land where it was.
/// </summary>
/// <remarks>
/// Through JSON, because that is how it travels. This is the piece that lets clips ride in a pack
/// at all - without it an editor would have to decode <c>.mab</c> itself, which is the one thing
/// the pack exists to prevent.
/// <para>
/// The four bone bitmasks are not carried; they are derived from which bones hold data. So a bank
/// coming back with its framing intact also says the derivation agrees with what shipped.
/// </para>
/// </remarks>
public sealed class BankDocumentTests
{
    // The options a pack actually writes with, so this covers the shape that ships rather than a
    // shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    /// <summary>
    /// A bank through the document a pack carries, and back to the same bytes.
    /// </summary>
    /// <remarks>
    /// Exact, not approximate, because the document carries each section verbatim alongside the
    /// decoded fields. That is the whole point: a bank holds the character's motion as well as the
    /// model's, and an editor rewriting a weapon's reload must not perturb the arms holding it.
    /// </remarks>
    [Theory]
    [InlineData(MabFixtures.Reload)]
    [InlineData(MabFixtures.Tie)]
    public void A_bank_survives_the_trip_through_json(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        string text = JsonSerializer.Serialize(BankDocument.From(MabFile.Parse(original)), Json);
        byte[] produced = JsonSerializer.Deserialize<BankDocument>(text, Json)!.ToMab();

        Fixture.AssertSameBytes(fixture, original, produced);
    }

    /// <summary>
    /// The same trip with the verbatim bytes thrown away, which is what an edited clip takes.
    /// </summary>
    /// <remarks>
    /// Without this the encoder would stop being tested the moment the document started carrying
    /// raw sections - every bank would pass by handing back what it was given. What is held here is
    /// that the decoded fields alone still rebuild the bank: its chain, its masks and its sections
    /// all where they were, and the bytes too unless a rotation ties.
    /// </remarks>
    [Theory]
    [InlineData(MabFixtures.Reload, true)]
    [InlineData(MabFixtures.Tie, false)]
    public void A_bank_rebuilds_from_its_decoded_fields_alone(string fixture, bool byteExact)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        BankDocument document = BankDocument.From(MabFile.Parse(original));
        foreach (ClipDocument clip in document.Clips)
        {
            clip.Raw.Clear();
            clip.Masks.Clear();
        }
        byte[] produced = document.ToMab();

        Assert.Equal(original.Length, produced.Length);
        Assert.True(SameShape(original, produced), $"{fixture}: a clip, section or mask moved.");
        if (byteExact)
        {
            Fixture.AssertSameBytes(fixture, original, produced);
        }
    }

    /// <summary>
    /// A participant's record names the clip that actually moves it.
    /// </summary>
    /// <remarks>
    /// The document says record <c>k</c> is chain clip <c>k + 1</c>, which is what lets a reader
    /// find the gun's motion without touching the tag block those records came from. So it is
    /// checked against where the records' own byte offsets land.
    /// </remarks>
    [Fact]
    public void A_participant_names_the_clip_that_moves_it()
    {
        if (Fixture.Read(MabFixtures.Reload) is not { } bytes)
        {
            return;
        }

        MabFile bank = MabFile.Parse(bytes);
        List<(MabParticipant Participant, MabClip Clip)> byOffset = bank.ParticipantClips();
        List<MabClip> chain = bank.Clips();
        List<BankParticipant> carried = BankDocument.From(bank).Participants;

        Assert.Equal(3, carried.Count);
        foreach (BankParticipant participant in carried)
        {
            Assert.InRange(participant.Clip, 1, chain.Count - 1);

            // Same bones and same sections is what "the same clip" means here - two clips in one
            // bank move different skeletons, so agreeing on both is not something a wrong index
            // gets away with.
            MabClip named = chain[participant.Clip];
            MabClip actual = byOffset[participant.Clip - 1].Clip;
            Assert.Equal(actual.BoneIds(), named.BoneIds());
            Assert.Equal(actual.Sections, named.Sections);
        }
    }

    /// <summary>
    /// Whether both banks hold the same chain, with each clip naming the same sections and the same
    /// bones - which is what the derived masks and the rebuilt nesting have to get right.
    /// </summary>
    private static bool SameShape(byte[] original, byte[] produced)
    {
        List<MabClip> before = MabFile.Parse(original).Clips();
        List<MabClip> after = MabFile.Parse(produced).Clips();
        if (before.Count != after.Count)
        {
            return false;
        }

        for (int index = 0; index < before.Count; index++)
        {
            if (!before[index].Sections.SequenceEqual(after[index].Sections)
                || !before[index].BoneIds().SequenceEqual(after[index].BoneIds()))
            {
                return false;
            }
        }
        return true;
    }
}
