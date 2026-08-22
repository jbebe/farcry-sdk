using System.Text.Json.Nodes;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// Compares this codec's decode of every shipped rig against the Python codec's, field by field.
/// </summary>
/// <remarks>
/// <see cref="SkeletonFileTests"/> proves the bytes survive a round trip, which a symmetric
/// misreading also passes - swap two same-width fields on the way in and back on the way out and
/// the file still reproduces. This is the gate that fails instead, and it is the evidence that lets
/// the Python implementation be deleted rather than merely matched on counts.
/// </remarks>
public sealed class SkeletonDifferentialTests
{
    private const string Format = "skeleton";

    [Fact]
    public void Decodes_every_shipped_rig_to_the_same_fields_as_the_python_codec()
        => Fc2FieldDump.AssertMatches(Format, data => Project(SkeletonFile.Parse(data)));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_field_dump_was_actually_found()
        => Assert.True(Fc2FieldDump.Present(Format), Fc2FieldDump.MissingMessage(Format));

    /// <summary>The decode in the shape `fielddump.py` emits, floats as their raw bits.</summary>
    private static JsonNode Project(SkeletonFile s) => new JsonObject
    {
        ["file_version"] = s.FileVersion,
        ["version"] = s.Version,
        ["scale_factor"] = Bits(s.ScaleFactor),
        ["common_bone_ids"] = Numbers(s.CommonBoneIds.Select(id => (long)id)),
        ["translation_bone_ids"] = Numbers(s.TranslationBoneIds.Select(id => (long)id)),
        ["lod_masks"] = new JsonArray([.. s.LodMasks.Select(mask => Numbers(mask.Select(v => (long)v)))]),
        ["bones"] = new JsonArray([.. s.Bones.Select(Project)]),
        ["handles"] = new JsonArray([.. s.Handles.Select(Project)]),
    };

    private static JsonNode Project(SkeletonBone b) => new JsonObject
    {
        ["name"] = b.Name,
        ["name_hash"] = b.NameHash,
        ["id"] = b.Id,
        ["parent"] = b.Parent,
        ["first_child"] = b.FirstChild,
        ["next_sibling"] = b.NextSibling,
        ["child_to_parent"] = BitArray(b.ChildToParent),
        ["local_offset"] = BitArray(b.LocalOffset),
        ["length"] = Bits(b.Length),
        ["ori"] = Project(b.Ori),
        ["pos"] = Project(b.Pos),
        ["animated_translation"] = b.AnimatedTranslation,
        ["body_part"] = b.BodyPart,
        ["com_weight"] = Bits(b.ComWeight),
        ["version"] = b.Version,
    };

    private static JsonNode Project(SkeletonAnimHandle h) => new JsonObject
    {
        ["id"] = h.Id,
        ["name"] = h.Name,
        ["name_hash"] = h.NameHash,
        ["parent_bone"] = h.ParentBone,
        ["parent_bone_hash"] = h.ParentBoneHash,
        ["child_to_parent"] = BitArray(h.ChildToParent),
        ["local_offset"] = BitArray(h.LocalOffset),
        ["parent_to_child"] = BitArray(h.ParentToChild),
        ["local_offset_inverted"] = BitArray(h.LocalOffsetInverted),
        ["parent_to_child_repeat"] = BitArray(h.ParentToChildRepeat),
        ["version"] = h.Version,
    };

    private static JsonNode Project(SkeletonConstraint c) => new JsonObject
    {
        ["kind"] = c.Kind,
        ["bones"] = Numbers(c.Bones.Select(bone => (long)bone)),
        ["weights"] = BitArray(c.Weights),
        ["offset"] = BitArray(c.Offset),
    };

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static JsonArray BitArray(float[] values) => Numbers(values.Select(v => (long)Bits(v)));

    private static JsonArray Numbers(IEnumerable<long> values)
        => new([.. values.Select(value => (JsonNode)JsonValue.Create(value))]);
}
