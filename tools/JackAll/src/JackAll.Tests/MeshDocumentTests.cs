using System.Text.Json;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// A shipped mesh decodes to the format-free document a <c>.fc2model</c> carries and builds back to
/// its bytes.
/// </summary>
/// <remarks>
/// This is the claim the whole pack design rests on - that a mesh can travel as names, transforms,
/// bounds and float-space geometry, with no Dunia bytes except the two words nothing derives and the
/// bodies of chunks nothing decodes. If it holds, an editor can carry no format code and still hand
/// back something the game loads.
/// </remarks>
public sealed class MeshDocumentTests
{
    // The options a pack actually writes with, so this covers the shape that ships rather than a
    // shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    [Theory]
    [MemberData(nameof(XbgFixtures.Formats), MemberType = typeof(XbgFixtures))]
    public void A_shipped_mesh_survives_the_trip_through_the_document(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        // Through JSON, because that is how it travels - a member the serialiser drops has to fail
        // here rather than in a mod nobody can explain.
        string text = JsonSerializer.Serialize(MeshDocument.From(XbgFile.Parse(original)), Json);
        Fixture.AssertSameBytes(
            fixture, original, JsonSerializer.Deserialize<MeshDocument>(text, Json)!.ToXbg().Write());
    }

    /// <summary>
    /// The document holds no Dunia bytes beyond the two words nothing derives and the chunks nothing
    /// decodes - which is what makes it safe for an editor that knows no formats.
    /// </summary>
    [Fact]
    public void The_document_carries_no_container_bookkeeping()
    {
        if (Fixture.Read(XbgFixtures.Ak47) is not { } bytes)
        {
            return;
        }

        MeshDocument document = MeshDocument.From(XbgFile.Parse(bytes));

        // Only the two rare chunks travel as bytes; the ten mandatory ones are all decoded.
        Assert.All(document.Chunks, chunk => Assert.Empty(chunk.Body));

        Assert.Equal(11, document.Parts.Count);
        Assert.Equal(9, document.Nodes.Count);
        Assert.Equal(5, document.Lods.Count);
        Assert.Contains(document.Nodes, node => node.Name == "FX_FIRE");

        // Geometry is float space: a rifle is under a couple of metres in every direction.
        foreach (MeshGeometry geometry in document.Lods[0].Geometry)
        {
            Assert.Equal(geometry.VertexCount * 3, geometry.Positions.Length);
            Assert.All(geometry.Positions, coordinate => Assert.True(Math.Abs(coordinate) < 2.0f));
        }
    }
}
