using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Mgb;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// References inside a `.mgb` Magma UI package: texture paths, every <c>NameId</c> the format
/// carries, and localised-string ids.
/// </summary>
/// <remarks>
/// The package is walked through its own <see cref="IMgbCodec"/> visitor rather than by hand. Every
/// record in the model already describes itself via <c>Serialize</c> - that is how the binary codec
/// and the XML codec are both built - so a third implementation that simply *records* what it is
/// shown gets a complete traversal of every widget class, keyframe state and nested link for free,
/// and cannot go stale when a widget gains a field. Walking the object graph by hand would mean
/// touching all ~20 widget types and re-touching them forever.
/// </remarks>
public sealed class MgbReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "mgb";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        MgbPackage package = MgbPackage.Read(content);
        var codec = new MgbReferenceCodec(sink);
        package.SerializeBody(codec);
    }
}

/// <summary>
/// An <see cref="IMgbCodec"/> that writes nothing and reads nothing - it just reports every name
/// hash, path string and string-resource id the package hands it, tagged with the field and scope it
/// came from.
/// </summary>
/// <remarks>
/// Runs in *write* direction (<see cref="IsReading"/> false) so records serialize from their live
/// values, exactly as the XML writer does; a reading codec would try to fill them in from a stream
/// this class doesn't have.
///
/// The site is "<c>Scope/field</c>" - the innermost scope name plus the field name, e.g.
/// <c>AreaLink/PACKAGE</c> or <c>USERDATA/key</c>. Deliberately not the full path with list indices:
/// site names are interned into a shared vocabulary (see <see cref="ReferenceSink.Intern"/>),
/// and indices would make that vocabulary unbounded.
/// </remarks>
internal sealed class MgbReferenceCodec(ReferenceSink sink) : IMgbCodec
{
    private readonly ReferenceSink _sink = sink;
    private readonly Stack<string> _scopes = new();

    /// <summary>
    /// The most recent <c>TABLEID</c>, waiting for the <c>RESOURCEID</c> that follows it.
    /// <c>StringResourceExternalId</c> writes the pair as two separate <c>U32</c> calls, so the id
    /// only becomes meaningful once both have been seen - and it's the resource id that identifies
    /// the string, with the table id as its site.
    /// </summary>
    private uint? _pendingTableId;

    public bool IsReading => false;
    public int Position => 0;

    private string Site(string field)
        => _scopes.Count > 0 ? $"{_scopes.Peek()}/{field}" : field;

    public IDisposable Scope(string name)
    {
        _scopes.Push(name);
        return new PopScope(_scopes);
    }

    public IDisposable Item(string name) => Scope(name);

    public IDisposable ListScope(string name, ref int count, MgbCountWidth width = MgbCountWidth.U32)
        => Scope(name);

    public int Count(string name, int count, MgbCountWidth width = MgbCountWidth.U32) => count;

    public bool Gate(string name, bool present) => present;

    public void NameId(string name, ref uint hash)
    {
        // StringResourceExternalId writes its TABLEID/RESOURCEID pair through NameId (they're u32s
        // on the wire like any other name hash), so the oasis case has to be recognised here rather
        // than treated as two unrelated name references. It's the resource id that identifies the
        // string; the table id becomes its site.
        switch (name)
        {
            case "TABLEID":
                _pendingTableId = hash;
                return;

            case "RESOURCEID" when _pendingTableId is { } table:
                _sink.AddNamed(RefSpace.OasisString, hash, RefKind.MgbStringResource, $"table:{table:X8}");
                _pendingTableId = null;
                return;
        }

        _sink.AddNamed(RefSpace.EngineName, hash, RefKind.MgbNameId, Site(name));
    }

    public void OptionalNameId(string name, ref uint? value)
    {
        if (value is { } hash)
        {
            _sink.AddNamed(RefSpace.EngineName, hash, RefKind.MgbNameId, Site(name));
        }
    }

    public void NameIdItems(string name, List<uint> values)
    {
        uint site = _sink.Intern(Site(name));
        for (int i = 0; i < values.Count; i++)
        {
            _sink.Add(RefSpace.EngineName, values[i], RefKind.MgbNameId, site, i);
        }
    }

    public void AnsiString(string name, ref byte[] value)
        => _sink.AddNamedPath(MgbText.Ansi(value), RefKind.MgbTexture, Site(name));

    // ---- everything below carries no reference; the interface still has to be satisfied ----

    public void U32(string name, ref uint value) { }
    public void U8(string name, ref byte value) { }
    public void U16(string name, ref ushort value) { }
    public void F32Bits(string name, ref uint bits) { }
    public void Bool(string name, ref bool value) { }
    public void Utf16String(string name, ref byte[] value) { }
    public void Blob(string name, ref byte[] value, int byteCount) { }
    public void EnumU32(string name, ref uint value, MgbEnum group) { }
    public void ColorU32(string name, ref uint argb) { }
    public void TypeSlot(string slotName, string className, ref byte slot, MgbTypeTable types) { }
    public void OptionalU32(string name, ref uint? value) { }
    public void OptionalBlob(string name, ref byte[]? value, int byteCount) { }
    public void U16Array(string name, ushort[] values) { }
    public void U32Array(string name, uint[] values) { }
    public void F32BitsArray(string name, uint[] values) { }
    public void U32Items(string name, List<uint> values) { }

    private sealed class PopScope(Stack<string> scopes) : IDisposable
    {
        public void Dispose() => scopes.Pop();
    }
}
