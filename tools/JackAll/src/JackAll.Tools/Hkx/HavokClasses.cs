using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Reflection;
using System.Text;
using System.Text.Json;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>Havok 5.5's <c>hkClassMember::Type</c>, in its own numbering.</summary>
public enum HkType : byte
{
    Void, Bool, Char, Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64, Real, Vector4, Quaternion,
    Matrix3, Rotation, QsTransform, Matrix4, Transform, Zero, Pointer, FunctionPointer, Array, InplaceArray,
    Enum, Struct, SimpleArray, HomogeneousArray, Variant, CString, ULong, Flags,
}

/// <summary>An enum the reflection declares, with its items as (value, name).</summary>
public sealed record HkEnum(string Name, IReadOnlyList<(int Value, string Name)> Items);

/// <summary>
/// A value an object stores inline, at <see cref="Offset"/> from its start: a member, or one element of a fixed
/// C array, with embedded structs flattened into theirs. <see cref="Path"/> reads like <c>motion.motionState.transform</c>
/// or <c>entities[1]</c>.
/// </summary>
public sealed record HkSlot(string Path, int Offset, int Size, HkMember Member);

/// <summary>A declared member. <see cref="Count"/> is a fixed C array's length, 0 for a scalar.</summary>
public sealed record HkMember(
    string Name, HkType Type, HkType Subtype, string? Class, HkEnum? Enum, short Count, ushort Flags, ushort Offset)
{
    internal const int SerializeIgnored = 0x400;

    /// <summary>Runtime-only state Havok zeroes rather than saves.</summary>
    public bool Ignored => (Flags & SerializeIgnored) != 0;
}

public sealed class HkClass
{
    private IReadOnlyList<HkMember>? _members;

    public required string Name { get; init; }
    public HkClass? Parent { get; internal set; }
    public required int Size { get; init; }
    public required int Interfaces { get; init; }
    public required IReadOnlyList<HkEnum> Enums { get; init; }
    public required IReadOnlyList<HkMember> DeclaredMembers { get; init; }

    /// <summary><c>hkClass::getSignature</c>, which a packfile lists beside the class's name.</summary>
    public uint Signature { get; internal set; }

    /// <summary>Inherited members first, so offsets ascend.</summary>
    public IReadOnlyList<HkMember> Members => _members ??= [.. Parent?.Members ?? [], .. DeclaredMembers];

    public override string ToString() => Name;
}

/// <summary>
/// Every class Dunia.dll can load from an <c>.hkx</c>, as its own <c>hkClass</c> reflection describes
/// it. Dumped by <c>tools/fc2re/DumpHavokClasses.java</c> into the embedded <c>HavokClasses.json</c>.
/// </summary>
public sealed class HavokClasses
{
    private readonly Dictionary<string, HkClass> _classes;
    private readonly ConcurrentDictionary<HkClass, IReadOnlyList<HkSlot>> _slots = [];

    private HavokClasses(Dictionary<string, HkClass> classes) => _classes = classes;

    public static HavokClasses Default { get; } = Load();

    public HkClass this[string name]
        => _classes.TryGetValue(name, out HkClass? c) ? c : throw new KeyNotFoundException($"Dunia.dll registers no Havok class '{name}'.");

    /// <summary>Bytes one value of this type takes in a 32-bit packfile.</summary>
    public int SizeOf(HkType type, HkType subtype, string? className) => type switch
    {
        HkType.Bool or HkType.Char or HkType.Int8 or HkType.UInt8 => 1,
        HkType.Int16 or HkType.UInt16 => 2,
        HkType.Int32 or HkType.UInt32 or HkType.Real or HkType.Pointer or HkType.FunctionPointer
            or HkType.CString or HkType.ULong => 4,
        HkType.Int64 or HkType.UInt64 or HkType.Variant or HkType.SimpleArray => 8,
        HkType.Vector4 or HkType.Quaternion => 16,
        HkType.Matrix3 or HkType.Rotation or HkType.QsTransform => 48,
        HkType.Matrix4 or HkType.Transform => 64,
        HkType.Array or HkType.HomogeneousArray => 12,
        HkType.Enum or HkType.Flags or HkType.Zero => SizeOf(subtype, HkType.Void, null),
        HkType.Struct => this[className!].Size,
        _ => throw new NotSupportedException($"No size for a Havok {type}."),
    };

    /// <summary>Every value the class stores inline, in member order.</summary>
    public IReadOnlyList<HkSlot> Slots(HkClass c) => _slots.GetOrAdd(c, k => [.. Flatten(k, "", 0)]);

    private IEnumerable<HkSlot> Flatten(HkClass c, string prefix, int at)
    {
        foreach (HkMember m in c.Members)
        {
            int size = SizeOf(m.Type, m.Subtype, m.Class);
            for (int k = 0; k < Math.Max(1, (int)m.Count); k++)
            {
                string path = prefix + (m.Count > 0 ? $"{m.Name}[{k}]" : m.Name);
                int offset = at + m.Offset + k * size;
                if (m.Type == HkType.Struct)
                {
                    foreach (HkSlot slot in Flatten(this[m.Class!], path + ".", offset))
                    {
                        yield return slot;
                    }
                }
                else
                {
                    yield return new HkSlot(path, offset, size, m);
                }
            }
        }
    }

    // hkClass::writeSignature for the class and then each parent; the signature is its CRC-32.
    private void WriteSignature(ByteWriter w, HkClass c)
    {
        for (HkClass? k = c; k is not null; k = k.Parent)
        {
            WriteDeclared(w, k);
        }
    }

    private void WriteDeclared(ByteWriter w, HkClass c)
    {
        w.WriteI32(c.Interfaces);
        foreach (HkEnum e in c.Enums)
        {
            WriteEnum(w, e);
        }
        w.WriteI32(c.Enums.Count);

        foreach (HkMember m in c.DeclaredMembers)
        {
            HkType type = m.Type, subtype = m.Subtype, extra = HkType.Void;
            int flags = m.Flags;
            if (type is HkType.Enum or HkType.Flags)
            {
                flags ^= SizeOf(subtype, HkType.Void, null) * 8;
                subtype = HkType.Void;
            }
            if (m.Ignored)
            {
                flags ^= HkMember.SerializeIgnored;
                (extra, type, subtype) = (subtype, HkType.Zero, type);
            }

            if (m.Class is not null && m.Type != HkType.Pointer && m.Subtype != HkType.Pointer)
            {
                WriteSignature(w, this[m.Class]);
            }
            if (m.Enum is not null)
            {
                WriteEnum(w, m.Enum);
            }

            w.WriteRaw(Encoding.ASCII.GetBytes(m.Name));
            w.WriteU16((ushort)type);
            w.WriteU16((ushort)subtype);
            if (extra != HkType.Void)
            {
                w.WriteU16((ushort)extra);
            }
            w.WriteI16(m.Count);
            w.WriteU16((ushort)flags);
        }
        w.WriteI32(c.DeclaredMembers.Count);
    }

    private static void WriteEnum(ByteWriter w, HkEnum e)
    {
        w.WriteRaw(Encoding.ASCII.GetBytes(e.Name));
        foreach ((int value, string name) in e.Items)
        {
            w.WriteRaw(Encoding.ASCII.GetBytes(name));
            w.WriteI32(value);
        }
        w.WriteI32(e.Items.Count);
    }

    private static HavokClasses Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JackAll.Tools.Hkx.HavokClasses.json")
            ?? throw new InvalidOperationException("HavokClasses.json is not embedded.");
        using JsonDocument doc = JsonDocument.Parse(stream);

        var enums = new Dictionary<string, HkEnum>();
        foreach (JsonProperty e in doc.RootElement.GetProperty("enums").EnumerateObject())
        {
            enums[e.Name] = new HkEnum(
                e.Value.GetProperty("name").GetString()!,
                [.. e.Value.GetProperty("items").EnumerateArray().Select(i => (i[0].GetInt32(), i[1].GetString()!))]);
        }

        var classes = new Dictionary<string, HkClass>();
        var parents = new Dictionary<HkClass, string>();
        foreach (JsonElement c in doc.RootElement.GetProperty("classes").EnumerateArray())
        {
            var hkClass = new HkClass
            {
                Name = c.GetProperty("name").GetString()!,
                Size = c.GetProperty("size").GetInt32(),
                Interfaces = c.GetProperty("interfaces").GetInt32(),
                Enums = [.. c.GetProperty("enums").EnumerateArray().Select(e => enums[e.GetString()!])],
                DeclaredMembers = [.. c.GetProperty("members").EnumerateArray().Select(m => new HkMember(
                    m.GetProperty("name").GetString()!,
                    (HkType)m.GetProperty("type").GetByte(),
                    (HkType)m.GetProperty("subtype").GetByte(),
                    m.GetProperty("class").GetString(),
                    m.GetProperty("enum").GetString() is { } id ? enums[id] : null,
                    m.GetProperty("count").GetInt16(),
                    m.GetProperty("flags").GetUInt16(),
                    m.GetProperty("offset").GetUInt16()))],
            };
            classes[hkClass.Name] = hkClass;
            if (c.GetProperty("parent").GetString() is { } parent)
            {
                parents[hkClass] = parent;
            }
        }
        foreach ((HkClass c, string parent) in parents)
        {
            c.Parent = classes[parent];
        }

        var registry = new HavokClasses(classes);
        foreach (HkClass c in classes.Values)
        {
            var w = new ByteWriter();
            registry.WriteSignature(w, c);
            c.Signature = Crc32.HashToUInt32(w.ToArray());
        }
        return registry;
    }
}
