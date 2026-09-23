using System.Text.Json;
using JackAll.Tools.Fc2Model;

namespace JackAll.Tests;

/// <summary>
/// A shipped material decodes to the format-free document a <c>.fc2model</c> carries and builds
/// back to its bytes.
/// </summary>
/// <remarks>
/// It runs the document through JSON on the way, because that is how it actually travels - so a
/// property the serialiser drops or reorders fails here rather than in a mod nobody can explain.
/// </remarks>
public sealed class MaterialDocumentTests
{
    // The options a pack actually writes with, so this covers the shape that ships rather than a
    // shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    [Theory]
    [InlineData(XbmFixtures.Wood)]
    [InlineData(XbmFixtures.Blended)]
    [InlineData(XbmFixtures.RepeatedKey)]
    public void A_shipped_material_survives_the_trip_through_json(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        string text = JsonSerializer.Serialize(MaterialDocument.Parse(original), Json);
        Fixture.AssertSameBytes(fixture, original, JsonSerializer.Deserialize<MaterialDocument>(text, Json)!.ToXbm());
    }
}
