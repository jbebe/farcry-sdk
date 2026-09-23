using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// A shipped `.skeleton` rig re-serialises to its own bytes.
/// </summary>
/// <remarks>
/// <c>Write(Parse(x)) == x</c> proves the reader and the writer at once, which matters here because
/// the format has no chunk lengths - a bone's constraint payload is sized by a one-byte kind, so a
/// wrong width silently reinterprets every bone after it rather than throwing.
/// </remarks>
public sealed class SkeletonFileTests
{
    [Theory]
    [InlineData(MabFixtures.CharacterRig)]
    [InlineData(MabFixtures.RifleRig)]
    public void Reserialises_a_shipped_rig_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        Fixture.AssertSameBytes(fixture, original, SkeletonFile.Parse(original).Write());
    }

    /// <summary>
    /// The human rig, as a named check that the decode means something rather than merely surviving.
    /// </summary>
    [Fact]
    public void The_character_rig_parses_to_the_recorded_shape()
    {
        if (Fixture.Read(MabFixtures.CharacterRig) is not { } bytes)
        {
            return;
        }

        SkeletonFile skeleton = SkeletonFile.Parse(bytes);

        Assert.Equal(119, skeleton.Bones.Count);
        Assert.Equal(30, skeleton.Handles.Count);

        // Translation is animated on exactly these two, which is what a .mab's separate
        // translation masks have to agree with.
        string[] translating = [.. skeleton.TranslationBoneIds
            .Where(id => id != SkeletonFile.NoBone)
            .Select(id => skeleton.Bones[id].Name)];
        Assert.Equal(["Pelvis", "Camera"], translating);

        Assert.NotNull(skeleton.BoneByName("R Hand"));
    }

    /// <summary>Sibling links are derived from each bone's parent, so rebuilding must be a no-op.</summary>
    [Theory]
    [InlineData(MabFixtures.CharacterRig)]
    [InlineData(MabFixtures.RifleRig)]
    public void Rebuilding_the_hierarchy_reproduces_the_shipped_links(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        SkeletonFile skeleton = SkeletonFile.Parse(bytes);
        (ushort, ushort)[] before = [.. skeleton.Bones.Select(b => (b.FirstChild, b.NextSibling))];

        skeleton.RebuildHierarchy();

        (ushort, ushort)[] after = [.. skeleton.Bones.Select(b => (b.FirstChild, b.NextSibling))];
        Assert.Equal(before, after);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(MabFixtures.CharacterRig, MabFixtures.RifleRig);
}
