namespace JackAll.Tools.Move;

/// <summary>What one recorded primitive in a MOVE object is.</summary>
public enum MoveOpKind
{
    U8,
    U32,
    S32,
    F32,
    Str,
    Data,
    Raw,
    Version,

    /// <summary>A version site whose tag was absent, so the reader defaulted it to 0.</summary>
    NoVersion,
    PointerNew,
    PointerRef,
    PointerNull,
}

/// <summary>
/// One primitive, in the order the engine's Serialize wrote it. <see cref="Name"/> is the debug
/// string the matching Transfer call passes; it carries no bytes and exists so the XML can label
/// what it emits.
/// </summary>
public readonly struct MoveOp(MoveOpKind kind, string name, uint number, byte[]? bytes, MoveObject? target)
{
    public MoveOpKind Kind { get; } = kind;
    public string Name { get; } = name;

    /// <summary>The value of an integer or version op.</summary>
    public uint Number { get; } = number;

    /// <summary>The payload of a float, string, data or raw op.</summary>
    public byte[]? Bytes { get; } = bytes;

    /// <summary>The object a pointer op points at.</summary>
    public MoveObject? Target { get; } = target;

    public static MoveOp Integer(MoveOpKind kind, string name, uint value) =>
        new(kind, name, value, null, null);

    public static MoveOp Blob(MoveOpKind kind, string name, byte[] value) =>
        new(kind, name, 0, value, null);

    public static MoveOp Pointer(MoveOpKind kind, string name, MoveObject? target) =>
        new(kind, name, 0, null, target);

    public MoveOp WithNumber(uint value) => new(Kind, Name, value, Bytes, Target);
}

/// <summary>One serialized object: its class and the ordered primitives it holds.</summary>
public sealed class MoveObject(string className)
{
    public string ClassName { get; } = className;

    public List<MoveOp> Ops { get; } = [];

    /// <summary>Position in registration order, which is how the file addresses it.</summary>
    public int Index { get; set; } = -1;

    /// <summary>The value of the first op carrying this field name.</summary>
    public uint? Field(string name)
    {
        foreach (MoveOp op in Ops)
        {
            if (op.Name == name)
            {
                return op.Number;
            }
        }

        return null;
    }

    public bool SetField(string name, uint value)
    {
        for (int i = 0; i < Ops.Count; i++)
        {
            if (Ops[i].Name == name)
            {
                Ops[i] = Ops[i].WithNumber(value);
                return true;
            }
        }

        return false;
    }

    public override string ToString() => $"{ClassName} #{Index}";
}

/// <summary>A parsed MOVE graph: the header, the root pointer, and every object in stream order.</summary>
public sealed class MoveFile
{
    public uint Type { get; set; }
    public uint Version { get; set; }

    /// <summary>The serializer's feature flags, and the reason a named twin will not load.</summary>
    public uint Flags { get; set; }

    /// <summary>Holds the single root pointer op; not itself a serialized object.</summary>
    public MoveObject Root { get; set; } = new("#file");

    public List<MoveObject> Objects { get; } = [];

    public bool IsNamed => (Flags & MoveFlags.Named) != 0;

    public MoveObject? StateMachine =>
        Objects.FirstOrDefault(o => o.ClassName == "CMoveStateMachine");
}

public sealed class MoveFormatException(string message) : Exception(message);

public static class MoveText
{
    /// <summary>
    /// A string field as readable ASCII, or null when it holds bytes that are not text - which some
    /// shipped fields do, most of the graph's Extension entries being uninitialised exporter stack.
    /// </summary>
    public static string? Printable(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            if (b is < 0x20 or > 0x7E)
            {
                return null;
            }
        }

        return System.Text.Encoding.ASCII.GetString(bytes);
    }
}
