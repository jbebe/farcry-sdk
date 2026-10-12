using System.Text;
using JackAll.Core.Format;

namespace JackAll.Tools.Hkx;

/// <summary>Where a pointer slot inside a block points.</summary>
public abstract class HkxRef;

/// <summary>Data the block owns and the writer places right after it: array elements or a string.</summary>
public sealed class HkxData(HkxBlock block) : HkxRef
{
    public HkxBlock Block { get; } = block;
}

public sealed class HkxPointer(HkxObject target) : HkxRef
{
    public HkxObject Target { get; set; } = target;
}

/// <summary>
/// A variant's class: an <c>hkClass</c> in <see cref="HkxFile.Types"/>, or null where the file stores
/// no types and Havok left a dead fixup entry instead.
/// </summary>
public sealed class HkxVariantClass(int? typesOffset) : HkxRef
{
    public int? TypesOffset { get; } = typesOffset;
}

/// <summary>A section kept verbatim: its body, then its three fixup tables at the given offsets.</summary>
public sealed record HkxRawSection(byte[] Bytes, int Locals, int Globals, int Virtuals);

/// <summary>A run of <c>__data__</c> bytes and its pointer slots by offset; pointer bytes stay zero.</summary>
public class HkxBlock(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;

    public SortedDictionary<int, HkxRef> Refs { get; } = [];
}

public sealed class HkxObject(HkClass hkClass, byte[] bytes) : HkxBlock(bytes)
{
    public HkClass Class { get; } = hkClass;

    public override string ToString() => Class.Name;
}

/// <summary>
/// A Dunia <c>.hkx</c>: a 16-byte prefix the engine skips, then a Havok 5.5.0 binary packfile in the
/// 32-bit little-endian layout. See docs/docs/file-formats/hkx.md.
/// </summary>
/// <remarks>
/// The writer places data as Havok's did: depth-first from <see cref="Root"/>, each object followed by
/// the data it owns, every block 16-aligned.
/// </remarks>
public sealed class HkxFile
{
    private const uint Magic0 = 0x57E0E057, Magic1 = 0x10C0C010;
    private const int PrefixSize = 16, HeaderSize = 0x40, SectionHeaderSize = 0x30;
    private const int FileVersion = 5, NamesSection = 0, TypesSection = 1, DataSection = 2;
    private const byte NameMarker = 9;
    private const string ContentsVersion = "Havok-5.5.0-r1";
    private static readonly byte[] Layout = [4, 1, 0, 1];

    public required byte[] Prefix { get; init; }

    /// <summary>26 in every shipped file; the version <c>CPhysResource</c> reports.</summary>
    public required uint UserTag { get; init; }

    /// <summary>The name table's order as shipped. Classes the objects need and it lacks are appended on write.</summary>
    public required List<string> ClassNames { get; init; }

    /// <summary>Class reflection, which only the seven animal ragdolls store; empty elsewhere.</summary>
    public required HkxRawSection Types { get; init; }

    public required HkxObject Root { get; init; }

    public static HkxFile Parse(ReadOnlySpan<byte> file) => new Reader(HavokClasses.Default).Read(file);

    public byte[] Write() => new Writer(this, HavokClasses.Default).Write();

    /// <summary>Every object reachable from the root, in the order the writer places them.</summary>
    public List<HkxObject> Objects()
    {
        var order = new List<HkxObject>();
        var seen = new HashSet<HkxObject>();
        Visit(Root);
        return order;

        void Visit(HkxObject o)
        {
            if (seen.Add(o))
            {
                order.Add(o);
                foreach (HkxObject target in Pointers(o))
                {
                    Visit(target);
                }
            }
        }
    }

    /// <summary>The objects a block points at, through the data it owns, in slot order.</summary>
    private static IEnumerable<HkxObject> Pointers(HkxBlock block) => block.Refs.Values.SelectMany(r => r switch
    {
        HkxPointer p => [p.Target],
        HkxData d => Pointers(d.Block),
        _ => Enumerable.Empty<HkxObject>(),
    });

    private static string CString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    /// <summary>A fixed-width ASCII field whose last byte is 0xFF, as Havok pads its tags.</summary>
    private static byte[] Fixed(string text, int size)
    {
        byte[] field = new byte[size];
        Encoding.ASCII.GetBytes(text).CopyTo(field, 0);
        field[^1] = 0xFF;
        return field;
    }

    private sealed class Reader(HavokClasses classes)
    {
        private readonly Dictionary<int, string> _nameAt = [];
        private readonly Dictionary<int, int> _locals = [];
        private readonly Dictionary<int, (int Section, int Target)> _globals = [];
        private readonly Dictionary<int, HkxObject> _objects = [];
        private byte[] _data = [];

        public HkxFile Read(ReadOnlySpan<byte> file)
        {
            if (file.Length < PrefixSize + HeaderSize || ByteCursor.U32(file, PrefixSize) != Magic0 || ByteCursor.U32(file, PrefixSize + 4) != Magic1)
            {
                throw new InvalidDataException("Not a Dunia .hkx: no Havok packfile 16 bytes in.");
            }
            ReadOnlySpan<byte> h = file[PrefixSize..];
            if (ByteCursor.I32(h, 12) != FileVersion || !h.Slice(16, 4).SequenceEqual(Layout) || CString(h.Slice(0x28, 16)) != ContentsVersion)
            {
                throw new InvalidDataException($"Expected a 32-bit little-endian {ContentsVersion} packfile, version {FileVersion}.");
            }

            var classNames = new List<string>();
            var types = new HkxRawSection([], 0, 0, 0);
            for (int i = 0; i < ByteCursor.I32(h, 0x14); i++)
            {
                int at = HeaderSize + i * SectionHeaderSize, start = ByteCursor.I32(h, at + 20);
                switch (CString(h.Slice(at, 20)))
                {
                    case "__classnames__":
                        ReadNames(h.Slice(start, ByteCursor.I32(h, at + 24)), classNames);
                        break;
                    case "__types__":
                        types = new HkxRawSection(
                            h.Slice(start, ByteCursor.I32(h, at + 44)).ToArray(), ByteCursor.I32(h, at + 24), ByteCursor.I32(h, at + 28), ByteCursor.I32(h, at + 32));
                        break;
                    case "__data__":
                        ReadData(h, start, at);
                        break;
                }
            }

            foreach ((int offset, HkxObject o) in _objects)
            {
                WalkStruct(o, offset, offset, o.Class);
            }
            if (_locals.Count + _globals.Count > 0)
            {
                throw new InvalidDataException($"{_locals.Count + _globals.Count} fixups point from no member the class layouts know.");
            }

            return new HkxFile
            {
                Prefix = file[..PrefixSize].ToArray(),
                UserTag = ByteCursor.U32(h, 8),
                ClassNames = classNames,
                Types = types,
                Root = _objects[ByteCursor.I32(h, 0x1C)],
            };
        }

        private void ReadNames(ReadOnlySpan<byte> body, List<string> names)
        {
            for (int i = 0; i + 5 < body.Length && body[i + 4] == NameMarker;)
            {
                string name = CString(body[(i + 5)..]);
                names.Add(name);
                _nameAt[i + 5] = name;
                i += 5 + name.Length + 1;
            }
        }

        private void ReadData(ReadOnlySpan<byte> h, int start, int header)
        {
            int local = ByteCursor.I32(h, header + 24), global = ByteCursor.I32(h, header + 28);
            int virt = ByteCursor.I32(h, header + 32), exports = ByteCursor.I32(h, header + 36);
            _data = h.Slice(start, local).ToArray();
            for (int at = start + local; at + 8 <= start + global; at += 8)
            {
                if (ByteCursor.U32(h, at) != uint.MaxValue)
                {
                    _locals[ByteCursor.I32(h, at)] = ByteCursor.I32(h, at + 4);
                }
            }
            for (int at = start + global; at + 12 <= start + virt; at += 12)
            {
                if (ByteCursor.U32(h, at) != uint.MaxValue)
                {
                    _globals[ByteCursor.I32(h, at)] = (ByteCursor.I32(h, at + 4), ByteCursor.I32(h, at + 8));
                }
            }
            for (int at = start + virt; at + 12 <= start + exports; at += 12)
            {
                if (ByteCursor.U32(h, at) != uint.MaxValue)
                {
                    int offset = ByteCursor.I32(h, at);
                    HkClass c = classes[_nameAt[ByteCursor.I32(h, at + 8)]];
                    _objects[offset] = new HkxObject(c, _data[offset..(offset + c.Size)]);
                }
            }
        }

        // `at` is a data-section offset; `blockAt` is where the block holding it starts.
        private void WalkStruct(HkxBlock block, int blockAt, int at, HkClass c)
        {
            foreach (HkMember m in c.Members)
            {
                int size = classes.SizeOf(m.Type, m.Subtype, m.Class);
                for (int k = 0; k < Math.Max(1, (int)m.Count); k++)
                {
                    WalkValue(block, blockAt, at + m.Offset + k * size, m.Type, m.Subtype, m.Class);
                }
            }
        }

        private void WalkValue(HkxBlock block, int blockAt, int at, HkType type, HkType subtype, string? className)
        {
            switch (type)
            {
                case HkType.Struct:
                    WalkStruct(block, blockAt, at, classes[className!]);
                    break;
                case HkType.Pointer or HkType.CString:
                    Slot(block, blockAt, at, type);
                    break;
                case HkType.Variant:
                    Slot(block, blockAt, at, HkType.Pointer);
                    if (_globals.Remove(at + 4, out (int Section, int Target) cls))
                    {
                        block.Refs[at + 4 - blockAt] = new HkxVariantClass(
                            cls.Section == TypesSection ? cls.Target : throw new InvalidDataException($"A variant's class at 0x{at + 4:X} is outside __types__."));
                    }
                    else if (block.Refs.ContainsKey(at - blockAt))
                    {
                        block.Refs[at + 4 - blockAt] = new HkxVariantClass(null);
                    }
                    break;
                case HkType.Array or HkType.SimpleArray when ByteCursor.I32(_data, at + 4) > 0:
                    int count = ByteCursor.I32(_data, at + 4), size = classes.SizeOf(subtype, HkType.Void, className);
                    int dst = _locals.Remove(at, out int target) ? target
                        : throw new InvalidDataException($"A non-empty array at 0x{at:X} has no fixup.");
                    var elements = new HkxBlock(_data[dst..(dst + count * size)]);
                    block.Refs[at - blockAt] = new HkxData(elements);
                    for (int i = 0; i < count; i++)
                    {
                        WalkValue(elements, dst, dst + i * size, subtype, HkType.Void, className);
                    }
                    break;
            }
        }

        private void Slot(HkxBlock block, int blockAt, int at, HkType type)
        {
            if (_globals.Remove(at, out (int Section, int Target) g))
            {
                block.Refs[at - blockAt] = new HkxPointer(g.Section == DataSection && _objects.TryGetValue(g.Target, out HkxObject? o) ? o
                    : throw new InvalidDataException($"A pointer at 0x{at:X} targets no object."));
            }
            else if (_locals.Remove(at, out int dst))
            {
                if (type != HkType.CString)
                {
                    throw new NotSupportedException($"A pointer at 0x{at:X} into owned data other than a string.");
                }
                block.Refs[at - blockAt] = new HkxData(new HkxBlock(_data[dst..(Array.IndexOf(_data, (byte)0, dst) + 1)]));
            }
        }
    }

    private sealed class Writer(HkxFile file, HavokClasses classes)
    {
        private readonly ByteWriter _data = new();
        private readonly List<(int Source, int Target)> _locals = [];
        private readonly List<(int Source, HkxRef Target)> _globals = [];
        private readonly Dictionary<HkxObject, int> _objectAt = [];
        private int _dropped;

        public byte[] Write()
        {
            List<HkxObject> objects = file.Objects();
            foreach (HkxObject o in objects)
            {
                _data.Align(16, AlignFill.Zero);
                int at = _data.Length;
                _objectAt[o] = at;
                _data.WriteRaw(o.Bytes);
                WriteOwned(o, at);
            }
            _data.Align(16, AlignFill.Zero);
            (byte[] names, Dictionary<string, int> nameAt) = NameTable(objects);

            int localsAt = _data.Length;
            foreach ((int source, int target) in _locals)
            {
                _data.WriteI32(source);
                _data.WriteI32(target);
            }
            _data.Align(16, AlignFill.Ones);
            int globalsAt = _data.Length;
            foreach ((int source, HkxRef target) in _globals)
            {
                (int section, int at) = target is HkxPointer p
                    ? (DataSection, _objectAt[p.Target])
                    : (TypesSection, ((HkxVariantClass)target).TypesOffset!.Value);
                _data.WriteI32(source);
                _data.WriteI32(section);
                _data.WriteI32(at);
            }
            for (int i = 0; i < _dropped * 3; i++)
            {
                _data.WriteI32(-1);
            }
            _data.Align(16, AlignFill.Ones);
            int virtualsAt = _data.Length;
            foreach (HkxObject o in objects)
            {
                _data.WriteI32(_objectAt[o]);
                _data.WriteI32(NamesSection);
                _data.WriteI32(nameAt[o.Class.Name]);
            }
            _data.Align(16, AlignFill.Ones);

            var w = new ByteWriter();
            w.WriteRaw(file.Prefix);
            w.WriteU32(Magic0);
            w.WriteU32(Magic1);
            w.WriteU32(file.UserTag);
            w.WriteI32(FileVersion);
            w.WriteRaw(Layout);
            w.WriteI32(3);
            w.WriteI32(DataSection);
            w.WriteI32(_objectAt[file.Root]);
            w.WriteI32(NamesSection);
            w.WriteI32(nameAt[file.Root.Class.Name]);
            w.WriteRaw(Fixed(ContentsVersion, 16));
            w.WriteI32(-1);
            w.WriteI32(-1);

            HkxRawSection types = file.Types;
            int namesAt = HeaderSize + 3 * SectionHeaderSize, typesAt = namesAt + names.Length, dataAt = typesAt + types.Bytes.Length;
            WriteSection(w, "__classnames__", namesAt, names.Length, names.Length, names.Length, names.Length);
            WriteSection(w, "__types__", typesAt, types.Locals, types.Globals, types.Virtuals, types.Bytes.Length);
            WriteSection(w, "__data__", dataAt, localsAt, globalsAt, virtualsAt, _data.Length);
            w.WriteRaw(names);
            w.WriteRaw(types.Bytes);
            w.WriteRaw(_data.ToArray());
            return w.ToArray();
        }

        private void WriteOwned(HkxBlock block, int at)
        {
            foreach ((int offset, HkxRef r) in block.Refs)
            {
                switch (r)
                {
                    case HkxData owned:
                        _data.Align(16, AlignFill.Zero);
                        int dst = _data.Length;
                        _locals.Add((at + offset, dst));
                        _data.WriteRaw(owned.Block.Bytes);
                        WriteOwned(owned.Block, dst);
                        break;
                    case HkxVariantClass { TypesOffset: null }:
                        _dropped++;
                        break;
                    default:
                        _globals.Add((at + offset, r));
                        break;
                }
            }
        }

        // The shipped table, then any class an object needs and it lacks, with what that class's own
        // members and parents need.
        private (byte[] Table, Dictionary<string, int> NameAt) NameTable(List<HkxObject> objects)
        {
            var names = new List<string>(file.ClassNames);
            var listed = names.ToHashSet();
            foreach (HkxObject o in objects)
            {
                Require(o.Class);
            }

            var w = new ByteWriter();
            var nameAt = new Dictionary<string, int>();
            foreach (string name in names)
            {
                w.WriteU32(classes[name].Signature);
                w.WriteU8(NameMarker);
                nameAt[name] = w.Length;
                w.WriteRaw(Encoding.ASCII.GetBytes(name));
                w.WriteU8(0);
            }
            w.Align(16, AlignFill.Ones);
            return (w.ToArray(), nameAt);

            void Require(HkClass c)
            {
                if (!listed.Add(c.Name))
                {
                    return;
                }
                names.Add(c.Name);
                foreach (string dependency in c.Members.Select(m => m.Class).OfType<string>())
                {
                    Require(classes[dependency]);
                }
                if (c.Parent is { } parent)
                {
                    Require(parent);
                }
            }
        }

        private static void WriteSection(ByteWriter w, string tag, int start, int locals, int globals, int virtuals, int end)
        {
            w.WriteRaw(Fixed(tag, 20));
            w.WriteI32(start);
            w.WriteI32(locals);
            w.WriteI32(globals);
            w.WriteI32(virtuals);
            w.WriteI32(end);
            w.WriteI32(end);
            w.WriteI32(end);
        }
    }
}
