namespace JackAll.Core.Format.Move;

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

    /// <summary>The same pointer aimed at another object - how a reference into a replaced state is
    /// re-seated once the replacement is in place.</summary>
    public MoveOp WithTarget(MoveObject target) => new(Kind, Name, Number, Bytes, target);
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

    /// <summary>The object the first pointer op carrying this field name points at.</summary>
    public MoveObject? FieldTarget(string name) => Ops.FirstOrDefault(op => op.Name == name).Target;

    /// <summary>The value of the first float op carrying this field name.</summary>
    public float? FieldF32(string name)
    {
        foreach (MoveOp op in Ops)
        {
            if (op.Name == name && op.Kind == MoveOpKind.F32)
            {
                return BitConverter.ToSingle(op.Bytes!);
            }
        }

        return null;
    }

    public bool SetFieldF32(string name, float value)
    {
        for (int i = 0; i < Ops.Count; i++)
        {
            if (Ops[i].Name == name && Ops[i].Kind == MoveOpKind.F32)
            {
                Ops[i] = MoveOp.Blob(MoveOpKind.F32, name, BitConverter.GetBytes(value));
                return true;
            }
        }

        return false;
    }

    /// <summary>This object and everything it owns, in the order a reader creates them.</summary>
    public IEnumerable<MoveObject> Subtree()
    {
        Stack<MoveObject> pending = new([this]);
        while (pending.TryPop(out MoveObject? node))
        {
            yield return node;
            for (int i = node.Ops.Count - 1; i >= 0; i--)
            {
                if (node.Ops[i].Kind == MoveOpKind.PointerNew)
                {
                    pending.Push(node.Ops[i].Target!);
                }
            }
        }
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

    /// <summary>The <c>CMoveMgr</c>, which an expansion such as <c>dlc1.bin</c> does not have.</summary>
    public MoveObject? Manager => Objects.FirstOrDefault(o => o.ClassName == "CMoveMgr");

    /// <summary>
    /// Rebuilds <see cref="Objects"/> by walking the ownership tree from the root, in the order a
    /// reader recreates them.
    /// </summary>
    /// <remarks>
    /// <see cref="MoveCodec.Save"/> walks the tree and never consults this list, so writing does not
    /// need it - but everything that <em>reasons</em> about a graph does, and after a splice the list
    /// still names the objects the edit replaced. Call this once the tree is final.
    /// </remarks>
    public void Reindex()
    {
        Objects.Clear();
        Objects.AddRange(Root.Subtree().Skip(1));
    }
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
