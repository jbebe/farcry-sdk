using System.Buffers.Binary;

namespace JackAll.Tools.Move;

/// <summary>Emits a MOVE graph, replaying the values the reader recorded.</summary>
/// <remarks>Back-references are written from the index this pass assigns, not the one the file was
/// read with, so an edited graph renumbers correctly.</remarks>
internal sealed class MoveWriteCodec(MoveFile file) : IMoveCodec
{
    private readonly List<byte> _out = [];
    private readonly Stack<(MoveObject Object, int Index)> _stack = new();
    private int _written;
    private MoveObject _current = file.Root;
    private int _index;

    public uint Flags => file.Flags;

    public byte[] WriteAll()
    {
        WriteU32(file.Type);
        WriteU32(file.Version);
        WriteU32(file.Flags);
        Pointer("root");
        return [.. _out];
    }

    private void WriteU32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _out.AddRange(buffer);
    }

    private MoveOp Next(MoveOpKind expected)
    {
        MoveOp op = Next();
        if (op.Kind != expected)
        {
            throw new MoveFormatException(
                $"op mismatch in {_current.ClassName} at {_index - 1}: have {op.Kind}, want {expected}");
        }

        return op;
    }

    private MoveOp Next()
    {
        if (_index >= _current.Ops.Count)
        {
            throw new MoveFormatException($"{_current.ClassName} ran out of ops at {_index}");
        }

        return _current.Ops[_index++];
    }

    public byte U8(string name)
    {
        byte value = (byte)Next(MoveOpKind.U8).Number;
        _out.Add(value);
        return value;
    }

    public uint U32(string name)
    {
        uint value = Next(MoveOpKind.U32).Number;
        WriteU32(value);
        return value;
    }

    public int S32(string name)
    {
        uint value = Next(MoveOpKind.S32).Number;
        WriteU32(value);
        return unchecked((int)value);
    }

    public void F32(string name) => _out.AddRange(Next(MoveOpKind.F32).Bytes!);

    public void Str(string name) => WriteBlob(Next(MoveOpKind.Str));

    public void Data(string name) => WriteBlob(Next(MoveOpKind.Data));

    public void Raw(string name, int count) => _out.AddRange(Next(MoveOpKind.Raw).Bytes!);

    private void WriteBlob(MoveOp op)
    {
        WriteU32((uint)op.Bytes!.Length);
        _out.AddRange(op.Bytes);
    }

    public uint Version(string name)
    {
        MoveOp op = Next();
        if (op.Kind == MoveOpKind.NoVersion)
        {
            return 0;
        }

        if (op.Kind != MoveOpKind.Version)
        {
            throw new MoveFormatException(
                $"expected a version op in {_current.ClassName}, got {op.Kind}");
        }

        WriteU32(MoveCodec.VersionTag);
        WriteU32(op.Number);
        return op.Number;
    }

    public MoveObject? Pointer(string name)
    {
        MoveOp op = Next();
        switch (op.Kind)
        {
            case MoveOpKind.PointerNull:
                WriteU32(unchecked((uint)-2));
                return null;

            case MoveOpKind.PointerRef:
                WriteU32(unchecked((uint)op.Target!.Index));
                return op.Target;

            case MoveOpKind.PointerNew:
                break;

            default:
                throw new MoveFormatException(
                    $"expected a pointer op in {_current.ClassName}, got {op.Kind}");
        }

        MoveObject target = op.Target!;
        WriteU32(unchecked((uint)-1));
        WriteU32(MoveClasses.Id(target.ClassName));
        target.Index = _written++;

        _stack.Push((_current, _index));
        _current = target;
        _index = 0;
        MoveLayout.Serialize(this, target.ClassName);
        (_current, _index) = _stack.Pop();
        return target;
    }
}
