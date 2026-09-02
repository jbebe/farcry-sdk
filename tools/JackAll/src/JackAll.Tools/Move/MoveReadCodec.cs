using System.Buffers.Binary;

namespace JackAll.Tools.Move;

/// <summary>Parses a MOVE graph, recording every primitive on the object being built.</summary>
internal sealed class MoveReadCodec(byte[] data, MoveFile file) : IMoveCodec
{
    private const int MaxBlob = 0x10000;

    private int _offset = 12;
    private MoveObject _current = file.Root;

    public uint Flags => file.Flags;

    public void ReadRoot()
    {
        Pointer("root");
        if (_offset != data.Length)
        {
            throw new MoveFormatException(
                $"short parse: 0x{_offset:x} of 0x{data.Length:x}");
        }
    }

    private void Need(int count)
    {
        if (_offset + count > data.Length)
        {
            throw new MoveFormatException($"ran off the end at 0x{_offset:x} (+{count})");
        }
    }

    private uint ReadU32()
    {
        Need(4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_offset));
        _offset += 4;
        return value;
    }

    private byte[] ReadBlob(MoveOpKind kind, string name)
    {
        int start = _offset;
        uint length = ReadU32();
        if (length > MaxBlob)
        {
            throw new MoveFormatException(
                $"absurd {kind} length {length} at 0x{start:x}");
        }

        Need((int)length);
        byte[] value = data.AsSpan(_offset, (int)length).ToArray();
        _offset += (int)length;
        _current.Ops.Add(MoveOp.Blob(kind, name, value));
        return value;
    }

    public byte U8(string name)
    {
        Need(1);
        byte value = data[_offset++];
        _current.Ops.Add(MoveOp.Integer(MoveOpKind.U8, name, value));
        return value;
    }

    public uint U32(string name)
    {
        uint value = ReadU32();
        _current.Ops.Add(MoveOp.Integer(MoveOpKind.U32, name, value));
        return value;
    }

    public int S32(string name)
    {
        int value = unchecked((int)ReadU32());
        _current.Ops.Add(MoveOp.Integer(MoveOpKind.S32, name, unchecked((uint)value)));
        return value;
    }

    // Kept as raw bytes so NaN and -0.0 survive a round trip.
    public void F32(string name)
    {
        Need(4);
        _current.Ops.Add(MoveOp.Blob(MoveOpKind.F32, name, data.AsSpan(_offset, 4).ToArray()));
        _offset += 4;
    }

    public void Str(string name) => ReadBlob(MoveOpKind.Str, name);

    public void Data(string name) => ReadBlob(MoveOpKind.Data, name);

    public void Raw(string name, int count)
    {
        Need(count);
        _current.Ops.Add(MoveOp.Blob(MoveOpKind.Raw, name, data.AsSpan(_offset, count).ToArray()));
        _offset += count;
    }

    public uint Version(string name)
    {
        if (_offset + 4 <= data.Length
            && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_offset)) == MoveCodec.VersionTag)
        {
            _offset += 4;
            uint value = ReadU32();
            _current.Ops.Add(MoveOp.Integer(MoveOpKind.Version, name, value));
            return value;
        }

        _current.Ops.Add(MoveOp.Integer(MoveOpKind.NoVersion, name, 0));
        return 0;
    }

    public MoveObject? Pointer(string name)
    {
        int at = _offset;
        int index = unchecked((int)ReadU32());
        if (index == -2)
        {
            _current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNull, name, null));
            return null;
        }

        if (index >= 0)
        {
            if (index >= file.Objects.Count)
            {
                throw new MoveFormatException(
                    $"back-reference {index} of {file.Objects.Count} objects at 0x{at:x}");
            }

            MoveObject existing = file.Objects[index];
            _current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerRef, name, existing));
            return existing;
        }

        uint classId = ReadU32();
        string className = MoveClasses.Name(classId)
            ?? throw new MoveFormatException($"unknown ClassType 0x{classId:X8} at 0x{_offset - 4:x}");

        MoveObject created = new(className) { Index = file.Objects.Count };
        file.Objects.Add(created);
        _current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNew, name, created));

        MoveObject parent = _current;
        _current = created;
        MoveLayout.Serialize(this, className);
        _current = parent;
        return created;
    }
}
