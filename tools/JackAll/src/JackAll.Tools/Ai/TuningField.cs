using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Ai;

public enum TuningFieldKind
{
    Number,
    Whole,
    Toggle,
    Choice,
}

/// <summary>
/// One tunable of an archetype: the member at <see cref="Path"/> below one of the entity's components,
/// named and explained for someone who has never seen the FCB tree.
/// </summary>
public sealed record TuningField(
    string Group, string Label, string Help, string[] Path, string Member,
    TuningFieldKind Kind = TuningFieldKind.Number, string[]? Choices = null)
{
    private readonly uint _member = FcbClassDefinitions.Crc32Ascii(Member);
    private readonly uint[] _path = [.. Path.Select(FcbClassDefinitions.Crc32Ascii)];

    public double? Read(FcbObject entity)
    {
        if (Owner(entity) is not { } owner || !owner.Values.ContainsKey(_member))
        {
            return null;
        }
        return Kind switch
        {
            TuningFieldKind.Number => FcbEntityFields.ReadFloat(owner, _member),
            TuningFieldKind.Toggle => FcbEntityFields.ReadBool(owner, _member) ? 1 : 0,
            _ => FcbEntityFields.ReadU32(owner, _member),
        };
    }

    /// <summary>Writes <paramref name="value"/>; false when this archetype doesn't carry the member.</summary>
    public bool Write(FcbObject entity, double value)
    {
        FcbObject? owner = Owner(entity);
        if (owner is null || !owner.Values.ContainsKey(_member))
        {
            return false;
        }
        owner.Values[_member] = Kind switch
        {
            TuningFieldKind.Number => BitConverter.GetBytes((float)value),
            TuningFieldKind.Toggle => [(byte)(value != 0 ? 1 : 0)],
            _ => BitConverter.GetBytes((uint)Math.Max(0, Math.Round(value))),
        };
        return true;
    }

    private FcbObject? Owner(FcbObject entity)
    {
        FcbObject? node = FcbEntityFields.FindComponent(entity, _path[0]);
        for (int i = 1; i < _path.Length && node is not null; i++)
        {
            node = node.Children.Find(c => c.TypeHash == _path[i]);
        }
        return node;
    }
}

/// <summary>
/// One kind of archetype the AI tab tunes: the fields it offers, a <see cref="Marker"/> only that
/// kind carries, and how its archetypes are grouped in the list.
/// </summary>
public sealed record TuningCatalog(IReadOnlyList<TuningField> Fields, TuningField Marker, Func<string, string> GroupOf)
{
    public bool Covers(FcbObject entity) => Marker.Read(entity) is not null;
}
