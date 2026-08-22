using System.Text.Json.Nodes;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// Compares this codec's decode of every shipped mesh and material against the Python codec's,
/// field by field.
/// </summary>
/// <remarks>
/// The container round trip proves the framing, which a symmetric misreading also passes - swap two
/// same-width fields on the way in and back on the way out and the file still reproduces. This is
/// the gate that fails instead. The mesh projection carries structure only: the vertex and index
/// blocks are what the round trip already covers, and dumping them would dwarf everything else.
/// </remarks>
public sealed class MeshDifferentialTests
{
    [Fact]
    public void Decodes_every_shipped_mesh_to_the_same_fields_as_the_python_codec()
        => Fc2FieldDump.AssertMatches("xbg", data => Project(XbgFile.Parse(data)));

    [Fact]
    public void Decodes_every_shipped_material_to_the_same_fields_as_the_python_codec()
        => Fc2FieldDump.AssertMatches("xbm", data => Project(XbmFile.Parse(data)));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_field_dumps_were_actually_found()
    {
        Assert.True(Fc2FieldDump.Present("xbg"), Fc2FieldDump.MissingMessage("xbg"));
        Assert.True(Fc2FieldDump.Present("xbm"), Fc2FieldDump.MissingMessage("xbm"));
    }

    private static JsonNode Project(XbgFile m) => new JsonObject
    {
        ["version"] = m.Version,
        ["header_words"] = Numbers(m.HeaderWords.Select(v => (long)v)),
        ["chunks"] = new JsonArray([.. m.Chunks.Select(c => (JsonNode)new JsonObject
        {
            ["tag"] = c.Tag,
            ["word0"] = c.Word0,
            ["raw_length"] = c.Raw.Length,
        })]),
        ["materials"] = new JsonArray([.. m.Materials.Select(name => (JsonNode)JsonValue.Create(name))]),
        ["material_word"] = m.MaterialWord is { } word ? JsonValue.Create(word) : null,
        ["cluster_word0"] = m.ClusterWord0,
        ["lod_words"] = Numbers(m.LodWords.Select(v => (long)v)),
        ["bbox"] = BitArray(m.Box),
        ["bsphere"] = BitArray(m.Sphere),
        ["pos_compress"] = BitArray(m.PosCompress),
        ["uv_compress"] = BitArray(m.UvCompress),
        ["bind_matrices"] = new JsonArray([.. m.BindMatrices.Select(matrix => (JsonNode)BitArray(matrix))]),
        ["nodes"] = new JsonArray([.. m.Nodes.Select(n => (JsonNode)new JsonObject
        {
            ["name"] = n.Name,
            ["name_hash"] = n.NameHash,
            ["first_child"] = n.FirstChild,
            ["next_sibling"] = n.NextSibling,
            ["parent"] = n.Parent,
            ["rotation"] = BitArray(n.Rotation),
            ["translation"] = BitArray(n.Translation),
            ["scale"] = BitArray(n.Scale),
            ["skin_index"] = n.SkinIndex,
            ["weight"] = Bits(n.Weight),
            ["extent"] = Bits(n.Extent),
        })]),
        ["part_refs"] = new JsonArray([.. m.PartRefs.Select(p => (JsonNode)new JsonObject
        {
            ["name_hash"] = p.NameHash,
            ["node"] = p.Node,
        })]),
        ["parts"] = new JsonArray([.. m.Parts.Select(d => (JsonNode)new JsonObject
        {
            ["name"] = d.Name,
            ["lod_metric"] = Bits(d.LodMetric),
            ["bounds"] = BitArray(d.Bounds),
            ["lod"] = d.Lod,
            ["reserved"] = d.Reserved,
            ["clusters"] = new JsonArray([.. d.Clusters.Select(c => (JsonNode)new JsonObject
            {
                ["material_index"] = c.MaterialIndex,
                ["face_count"] = c.FaceCount,
                ["stride"] = c.Stride,
                ["vertex_count"] = c.VertexCount,
                ["flags"] = c.Flags,
                ["palette"] = Numbers(c.Palette.Select(slot => (long)slot)),
            })]),
        })]),
        ["lods"] = new JsonArray([.. m.Lods.Select(lod => (JsonNode)new JsonObject
        {
            ["distance"] = Bits(lod.Distance),
            ["vertex_bytes"] = lod.VertexData.Length,
            ["index_bytes"] = lod.IndexData.Length,
            ["vertex_buffers"] = new JsonArray([.. lod.VertexBuffers.Select(b => (JsonNode)new JsonObject
            {
                ["flags"] = b.Flags,
                ["stride"] = b.Stride,
                ["vertex_count"] = b.VertexCount,
                ["offset"] = b.Offset,
            })]),
            ["submeshes"] = new JsonArray([.. lod.Submeshes.Select(s => (JsonNode)new JsonObject
            {
                ["buffer"] = s.Buffer,
                ["part"] = s.Part,
                ["cluster"] = s.Cluster,
                ["index_offset"] = s.IndexOffset,
                ["trailing"] = Numbers(s.Trailing.Select(v => (long)v)),
            })]),
        })]),
    };

    private static JsonNode Project(XbmFile x) => new JsonObject
    {
        ["name"] = x.Name,
        ["part"] = x.Part,
        ["shader"] = x.Shader,
        ["preamble"] = Numbers(x.Preamble.Select(b => (long)b)),
        ["trailing"] = x.Trailing,
        ["entries"] = new JsonArray([.. x.Entries.Select(Project)]),
    };

    private static JsonNode Project(XbmEntry entry)
    {
        var node = new JsonObject
        {
            ["section"] = entry.Section switch
            {
                XbmSection.Texture => "textures",
                XbmSection.Float => "floats",
                _ => "integers",
            },
            ["key"] = entry.Key,
        };
        switch (entry.Section)
        {
            case XbmSection.Texture:
                node["path"] = entry.Path;
                break;
            case XbmSection.Float:
                node["floats"] = BitArray(entry.Floats);
                break;
            default:
                node["integer"] = entry.Integer;
                break;
        }
        return node;
    }

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static JsonArray BitArray(float[] values) => Numbers(values.Select(v => (long)Bits(v)));

    private static JsonArray Numbers(IEnumerable<long> values)
        => new([.. values.Select(value => (JsonNode)JsonValue.Create(value))]);
}
