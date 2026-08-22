using System.Text.Json.Nodes;
using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// Compares this codec's decode of every shipped bank against the Python codec's, clip by clip.
/// </summary>
/// <remarks>
/// The round trip carries each clip's body as opaque bytes, so it proves nothing at all about the
/// masks, the sparse keyframe groups or the quaternion component layout - the three places a
/// misreading would hide here. This is the gate that covers them.
/// <para>
/// The corpus holds 14.9 million keys, far too many to carry as values, so each track travels as a
/// count and two FNV-1a digests over its raw bits. A digest that matches means every frame number
/// and every quaternion bit agreed.
/// </para>
/// </remarks>
public sealed class MabDifferentialTests
{
    private const string Format = "mab";

    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>Stands in for a track entry the codec could not decode to a rotation.</summary>
    private const uint Undecodable = 0xFFFFFFFF;

    [Fact]
    public void Decodes_every_shipped_bank_to_the_same_fields_as_the_python_codec()
        => Fc2FieldDump.AssertMatches(Format, data => Project(MabFile.Parse(data)));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_field_dump_was_actually_found()
        => Assert.True(Fc2FieldDump.Present(Format), Fc2FieldDump.MissingMessage(Format));

    private static JsonNode Project(MabFile bank) => new JsonObject
    {
        ["version"] = bank.Version,
        ["header"] = Numbers(bank.Header.Select(b => (long)b)),
        ["clips"] = new JsonArray([.. bank.Clips().Select(Project)]),
    };

    private static JsonNode Project(MabClip clip) => new JsonObject
    {
        ["masks"] = new JsonArray([.. clip.Masks.Select(mask => (JsonNode)Numbers(mask.Select(v => (long)v)))]),
        ["reference_rotation"] = BitArray(clip.ReferenceRotation),
        ["loop_rotation"] = BitArray(clip.LoopRotation),
        ["duration"] = Bits(clip.Duration),
        ["sections"] = Numbers(clip.Sections.Select(v => (long)v)),
        ["data_length"] = clip.Data.Length,
        ["constant_rotations"] = Constants(clip.ConstantRotations()),
        ["constant_translations"] = Constants(clip.ConstantTranslations()),
        ["keyframes"] = Tracks(clip.KeyframeTracks()),
        ["translations"] = Tracks(clip.TranslationTracks()),
        ["root_translation"] = TrackDigest(clip.RootTranslation()),
        ["root_rotation"] = TrackDigest(clip.RootRotation()),
        ["participants"] = new JsonArray([.. clip.Participants().Select(p => (JsonNode)new JsonObject
        {
            ["kind"] = p.Kind,
            ["name"] = p.Name,
            ["parent"] = p.Parent,
            ["reference"] = p.Reference,
            ["clip_offset"] = p.ClipOffset,
        })]),
    };

    private static JsonArray Constants(Dictionary<int, float[]> values)
        => new([.. values.OrderBy(pair => pair.Key).Select(pair => (JsonNode)new JsonObject
        {
            ["bone"] = pair.Key,
            ["value"] = BitArray(pair.Value),
        })]);

    private static JsonArray Tracks(Dictionary<int, List<(int Frame, float[]? Value)>> tracks)
        => new([.. tracks.OrderBy(pair => pair.Key).Select(pair =>
        {
            var node = (JsonObject)TrackDigest(pair.Value);
            // The bone comes first in the dump, so rebuild rather than append.
            return (JsonNode)new JsonObject
            {
                ["bone"] = pair.Key,
                ["count"] = node["count"]!.DeepClone(),
                ["frames"] = node["frames"]!.DeepClone(),
                ["values"] = node["values"]!.DeepClone(),
            };
        })]);

    private static JsonNode TrackDigest(List<(int Frame, float[]? Value)> entries)
    {
        List<long> frames = [];
        List<long> values = [];
        foreach ((int frame, float[]? value) in entries)
        {
            frames.Add(frame);
            if (value is null)
            {
                values.Add(Undecodable);
            }
            else
            {
                values.AddRange(value.Select(v => (long)Bits(v)));
            }
        }
        return new JsonObject
        {
            ["count"] = entries.Count,
            ["frames"] = Fnv(frames),
            ["values"] = Fnv(values),
        };
    }

    private static uint Fnv(IEnumerable<long> values)
    {
        uint hash = FnvOffset;
        foreach (long value in values)
        {
            hash = (hash ^ (uint)value) * FnvPrime;
        }
        return hash;
    }

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static JsonArray BitArray(float[] values) => Numbers(values.Select(v => (long)Bits(v)));

    private static JsonArray Numbers(IEnumerable<long> values)
        => new([.. values.Select(value => (JsonNode)JsonValue.Create(value))]);
}
