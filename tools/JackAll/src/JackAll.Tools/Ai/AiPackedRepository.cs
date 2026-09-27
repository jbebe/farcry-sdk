using JackAll.Core.Format;

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
        var r = new ByteCursor(data);
        var repo = new AiPackedRepository();

        uint blobs = r.ReadU32();
        for (uint i = 0; i < blobs; i++)
        {
            uint cls = r.ReadU32();
            repo.Blobs.Add(new AiParameterBlob(cls, r.ReadBytes((int)r.ReadU32())));
        }

        repo.Connections = r.ReadBytes((int)r.ReadU32());
        repo.Anchors.AddRange(r.ReadU32Array((int)r.ReadU32()));

        uint tasks = r.ReadU32();
        for (uint i = 0; i < tasks; i++)
        {
            (uint hash, string name) = r.ReadStringId();
            ushort blob = (ushort)r.ReadU32();
            uint offset = r.ReadU32();
            repo.Tasks.Add(new AiPackedTask(hash, name, blob, offset, (ushort)r.ReadU32()));
        }

        if (r.Remaining != 0)
        {
            throw new InvalidDataException($"Packed AI repository has {r.Remaining} trailing bytes.");
        }
        return repo;
    }

    public byte[] Write()
    {
        var w = new ByteWriter();
        w.WriteU32((uint)Blobs.Count);
        foreach (AiParameterBlob blob in Blobs)
        {
            w.WriteU32(blob.ClassHash);
            w.WriteU32((uint)blob.Rml.Length);
            w.WriteRaw(blob.Rml);
        }

        w.WriteU32((uint)Connections.Length);
        w.WriteRaw(Connections);
        w.WriteU32((uint)Anchors.Count);
        w.WriteU32Array([.. Anchors]);

        w.WriteU32((uint)Tasks.Count);
        foreach (AiPackedTask task in Tasks)
        {
            w.WriteStringId(task.Name, task.NameHash);
            w.WriteU32(task.Blob);
            w.WriteU32(task.ConnectionOffset);
            w.WriteU32(task.ConnectionLength);
        }
        return w.ToArray();
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

    /// <summary>The connection records of <paramref name="task"/>, as <c>CTaskRepository::GetTask</c> reads them.</summary>
    public IReadOnlyList<AiConnectionRecord> ConnectionsOf(AiPackedTask task)
    {
        var r = new ByteCursor(Slice(task));
        var records = new List<AiConnectionRecord>();
        while (r.Remaining > 0)
        {
            var kind = (AiConnectionKind)r.ReadU8();
            switch (kind)
            {
                case AiConnectionKind.Owner:
                    records.Add(new AiConnectionRecord(kind, r.ReadU16(), []));
                    break;
                case AiConnectionKind.Selectable:
                    ushort target = r.ReadU16();
                    records.Add(new AiConnectionRecord(kind, r.ReadU16(), [new AiConnectionTarget(target, 0, 0)]));
                    break;
                case AiConnectionKind.Anchor or AiConnectionKind.Exit or AiConnectionKind.Event:
                    ushort source = r.ReadU16();
                    var targets = new AiConnectionTarget[r.ReadU16()];
                    for (int i = 0; i < targets.Length; i++)
                    {
                        targets[i] = new AiConnectionTarget(r.ReadU16(), r.ReadU16(), r.ReadU8());
                    }
                    records.Add(new AiConnectionRecord(kind, source, targets));
                    break;
                default:
                    throw new InvalidDataException($"Unknown connection record kind {(byte)kind} in task {task.Name}.");
            }
        }
        return records;
    }

    private ReadOnlySpan<byte> Slice(AiPackedTask task) => Connections.AsSpan((int)task.ConnectionOffset, task.ConnectionLength);
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
