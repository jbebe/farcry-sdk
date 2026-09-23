using System.Text.Json;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// A shipped <c>.skeleton</c> serialised through a pack's JSON builds back to its bytes.
/// </summary>
/// <remarks>
/// A rig needs no separate document type - <see cref="SkeletonFile"/> is already decoded, carrying
/// names, rest poses, constraints and sockets rather than anything about framing. Serialising it
/// directly is what the pack does, so this is the shape that actually travels.
/// </remarks>
public sealed class RigDocumentTests
{
    // The options a pack actually writes with, so this covers the shape that ships rather than a
    // shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    [Theory]
    [InlineData(MabFixtures.CharacterRig)]
    [InlineData(MabFixtures.RifleRig)]
    public void A_shipped_rig_survives_the_trip_through_json(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        string text = JsonSerializer.Serialize(SkeletonFile.Parse(original), Json);
        Fixture.AssertSameBytes(fixture, original, JsonSerializer.Deserialize<SkeletonFile>(text, Json)!.Write());
    }
}
