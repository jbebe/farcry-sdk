namespace JackAll.Core.Format;

/// <summary>One decoded field on an <see cref="MgbNode"/> - a label and its already-formatted value.</summary>
public readonly record struct MgbField(string Label, string Value);

/// <summary>
/// A generic decoded node in a .mgb widget/package tree - deliberately weakly-typed (a class-name
/// string plus a field/children list) rather than one C# type per Magma class. With ~40 distinct
/// record shapes documented in reverse/dunia/mgb_format.md, a strongly-typed object model would be a
/// lot of code for a read-only inspector's actual job: showing what's in the file. See
/// <see cref="MgbBody"/> for the decoder that builds this tree.
/// </summary>
public sealed record MgbNode(string Kind, IReadOnlyList<MgbField> Fields, IReadOnlyList<MgbNode> Children)
{
    public static MgbNode Leaf(string kind, params MgbField[] fields) => new(kind, fields, []);
}
