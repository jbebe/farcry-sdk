using System.Buffers.Binary;
using System.Text;

namespace JackAll.Tools.Ai;

/// <summary>One distinct parameter block: a task class and its <c>Parameters</c> RML bytes.</summary>
public sealed record AiParameterBlob(uint ClassHash, byte[] Rml);

/// <summary>One task of a packed repository and its slice of the shared connection buffer.</summary>
public sealed record AiPackedTask(uint NameHash, string Name, ushort Blob, uint ConnectionOffset, ushort ConnectionLength);

/// <summary>
/// The compiled half of an <see cref="AiWorkspaceFile"/>, the form <c>CTaskRepository</c> loads:
/// deduplicated parameter blobs, one connection buffer, the anchor-name table and the task table
/// sorted by name hash.
/// </summary>
public sealed class AiPackedRepository
{
    public List<AiParameterBlob> Blobs { get; } = [];

    public byte[] Connections { get; set; } = [];

    /// <summary>CStringIDs of every anchor, exit, event and filter name a connection refers to.</summary>
    public List<uint> Anchors { get; } = [];

    public List<AiPackedTask> Tasks { get; } = [];

    public static AiPackedRepository Read(byte[] data)
    {
        var r = new Reader(data);
        var repo = new AiPackedRepository();

        uint blobs = r.U32();
        for (uint i = 0; i < blobs; i++)
        {
            uint cls = r.U32();
            repo.Blobs.Add(new AiParameterBlob(cls, r.Bytes((int)r.U32())));
        }

        repo.Connections = r.Bytes((int)r.U32());

        uint anchors = r.U32();
        for (uint i = 0; i < anchors; i++)
        {
            repo.Anchors.Add(r.U32());
        }

        uint tasks = r.U32();
        for (uint i = 0; i < tasks; i++)
        {
            uint name = r.U32();
            string text = Encoding.ASCII.GetString(r.Bytes((int)r.U32()));
            ushort blob = (ushort)r.U32();
            uint offset = r.U32();
            ushort length = (ushort)r.U32();
            repo.Tasks.Add(new AiPackedTask(name, text, blob, offset, length));
        }

        if (!r.AtEnd)
        {
            throw new InvalidDataException($"Packed AI repository has {data.Length - r.Position} trailing bytes.");
        }
        return repo;
    }

    public byte[] Write()
    {
        using var s = new MemoryStream();
        WriteU32(s, (uint)Blobs.Count);
        foreach (AiParameterBlob blob in Blobs)
        {
            WriteU32(s, blob.ClassHash);
            WriteU32(s, (uint)blob.Rml.Length);
            s.Write(blob.Rml);
        }

        WriteU32(s, (uint)Connections.Length);
        s.Write(Connections);

        WriteU32(s, (uint)Anchors.Count);
        foreach (uint anchor in Anchors)
        {
            WriteU32(s, anchor);
        }

        WriteU32(s, (uint)Tasks.Count);
        foreach (AiPackedTask task in Tasks)
        {
            WriteU32(s, task.NameHash);
            byte[] name = Encoding.ASCII.GetBytes(task.Name);
            WriteU32(s, (uint)name.Length);
            s.Write(name);
            WriteU32(s, task.Blob);
            WriteU32(s, task.ConnectionOffset);
            WriteU32(s, task.ConnectionLength);
        }
        return s.ToArray();
    }

    /// <summary>
    /// Where <paramref name="other"/> would load differently, or null when it loads the same. Where each
    /// task's slice sits in <see cref="Connections"/> is ignored: the engine reads it by offset.
    /// </summary>
    public string? FirstDifference(AiPackedRepository other)
    {
        if (Blobs.Count != other.Blobs.Count)
        {
            return $"{other.Blobs.Count} parameter blobs, expected {Blobs.Count}";
        }
        for (int i = 0; i < Blobs.Count; i++)
        {
            if (Blobs[i].ClassHash != other.Blobs[i].ClassHash || !Blobs[i].Rml.AsSpan().SequenceEqual(other.Blobs[i].Rml))
            {
                return $"parameter blob {i} differs";
            }
        }
        if (!Anchors.SequenceEqual(other.Anchors))
        {
            return $"anchor table differs ({other.Anchors.Count} anchors, expected {Anchors.Count})";
        }
        if (Tasks.Count != other.Tasks.Count)
        {
            return $"{other.Tasks.Count} tasks, expected {Tasks.Count}";
        }
        for (int i = 0; i < Tasks.Count; i++)
        {
            AiPackedTask a = Tasks[i], b = other.Tasks[i];
            if (a.NameHash != b.NameHash || a.Name != b.Name || a.Blob != b.Blob)
            {
                return $"task {i} ({a.Name}) differs in name or parameters";
            }
            if (!Slice(a).SequenceEqual(other.Slice(b)))
            {
                return $"task {i} ({a.Name}) differs in connections";
            }
        }
        return null;
    }

    private ReadOnlySpan<byte> Slice(AiPackedTask task) => Connections.AsSpan((int)task.ConnectionOffset, task.ConnectionLength);

    /// <summary>The connection records of <paramref name="task"/>, as <c>CTaskRepository::GetTask</c> reads them.</summary>
    public IReadOnlyList<AiConnectionRecord> ConnectionsOf(AiPackedTask task)
    {
        var r = new Reader(Slice(task).ToArray());
        var records = new List<AiConnectionRecord>();
        while (!r.AtEnd)
        {
            var kind = (AiConnectionKind)r.U8();
            switch (kind)
            {
                case AiConnectionKind.Owner:
                    records.Add(new AiConnectionRecord(kind, r.U16(), []));
                    break;
                case AiConnectionKind.Selectable:
                    ushort target = r.U16();
                    records.Add(new AiConnectionRecord(kind, r.U16(), [new AiConnectionTarget(target, 0, 0)]));
                    break;
                case AiConnectionKind.Anchor or AiConnectionKind.Exit or AiConnectionKind.Event:
                    ushort source = r.U16();
                    var targets = new AiConnectionTarget[r.U16()];
                    for (int i = 0; i < targets.Length; i++)
                    {
                        targets[i] = new AiConnectionTarget(r.U16(), r.U16(), r.U8());
                    }
                    records.Add(new AiConnectionRecord(kind, source, targets));
                    break;
                default:
                    throw new InvalidDataException($"Unknown connection record kind {(byte)kind} in task {task.Name}.");
            }
        }
        return records;
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        s.Write(b);
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }

        public bool AtEnd => Position >= data.Length;

        public byte U8() => data[Position++];

        public ushort U16()
        {
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position));
            Position += 2;
            return v;
        }

        public uint U32()
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position));
            Position += 4;
            return v;
        }

        public byte[] Bytes(int count)
        {
            byte[] v = data.AsSpan(Position, count).ToArray();
            Position += count;
            return v;
        }
    }
}

public enum AiConnectionKind : byte
{
    /// <summary>A brain's filter-to-plan entry; <see cref="AiConnectionRecord.Source"/> is the filter anchor.</summary>
    Selectable = 0,
    Anchor = 1,
    Exit = 2,
    Event = 3,
    /// <summary>Names the plan that adds this task; <see cref="AiConnectionRecord.Source"/> is its index.</summary>
    Owner = 4,
}

/// <summary>A connection's destination: a task index, an anchor index, and a flag byte.</summary>
public readonly record struct AiConnectionTarget(ushort Task, ushort Anchor, byte Flag);

public sealed record AiConnectionRecord(AiConnectionKind Kind, ushort Source, IReadOnlyList<AiConnectionTarget> Targets);
