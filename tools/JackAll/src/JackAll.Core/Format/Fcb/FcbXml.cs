using System.Globalization;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// The result of exporting an <see cref="FcbObject"/> tree to XML: a main "index" document, plus
/// zero or more external sub-documents it references by filename (Gibbed's "multi-export" splitting,
/// applied automatically for entity-library-shaped roots — see <see cref="FcbXml.ToXml"/>). Every
/// entry is keyed by the plain filename the index document's <c>external="..."</c> attribute uses.
/// </summary>
public sealed record FcbXmlExport(string IndexXml, IReadOnlyDictionary<string, string> ExternalFiles);

/// <summary>One fragment's <see cref="FcbXml.ToXml"/>-assigned id, paired with the byte length of
/// its rendered XML — see <see cref="FcbXml.ListFragmentsWithSize"/>.</summary>
public readonly record struct FcbFragmentInfo(string Id, long Size);

/// <summary>
/// Converts between a parsed <see cref="FcbObject"/> tree and Gibbed-compatible XML — same element
/// shape (`&lt;object type="..."|hash="..."&gt;`, `&lt;value name="..."|hash="..." type="..."&gt;`),
/// same value-type encodings, same "split an entity-library-of-groups into external files" behavior.
/// </summary>
/// <remarks>
/// <see cref="ToXml"/> is the only direction that consults <see cref="FcbClassDefinitions"/> — it's
/// purely a display aid, resolving hashes back to readable names/types where the config knows them.
/// <see cref="FromXml"/> never needs it: a value's type and a class/member's name are both read
/// directly off the XML (computing the CRC32 of a <c>name="..."</c>/<c>type="..."</c> attribute, or
/// parsing a <c>hash="..."</c> one directly) exactly the way Gibbed's own reader does, so hand-editing
/// the exported XML (including the documented "change type from BinHex to the real type" trick) round
/// trips correctly without this class ever needing to know what a field is called.
///
/// <see cref="FcbMemberType.Rml"/> values are a distinct binary format nested inside this one (Dunia's
/// ".rml", used standalone for DLC manifests too — see <see cref="RmlDocument"/>): when a value's
/// bytes actually parse as one, it's decoded into a real nested element instead of opaque hex, matching
/// what a more capable community converter (wobatt's) already does. Not every Rml-typed value is
/// necessarily a well-formed .rml document though, so a value that fails to parse falls back to the
/// same BinHex-shaped hex text as before — still fully editable, just not as readable nested XML.
/// </remarks>
public static class FcbXml
{
    private const uint EntityLibraryTypeHash = 0xBCDD10B4;
    private const uint EntityLibraryGroupTypeHash = 0xE0BDB3DB;
    private static readonly uint NameFieldHash = FcbClassDefinitions.Crc32Ascii("Name");

    /// <summary>
    /// Converts a parsed FCB tree to XML. When the tree matches Gibbed's "entity library made up of
    /// named groups" shape (root has no values, root's type is EntityLibrary, every child is an
    /// unnamed group object) — true of every real entitylibrary*.fcb sample seen — splits each child
    /// into its own external file, matching Gibbed's default multi-export behavior exactly (including
    /// its one quirk: each split-out child's own class is resolved against the flat top-level table,
    /// not nested inside the root's resolved class, since multi-export bypasses that scoping step).
    /// Otherwise everything is written inline in one document.
    /// </summary>
    public static FcbXmlExport ToXml(FcbObject root, FcbClassDefinitions defs)
    {
        if (!TryGetFragmentIds(root, out IReadOnlyList<string> ids))
        {
            (XElement inline, _) = WriteObject(root, defs);
            return new FcbXmlExport(Render(inline), new Dictionary<string, string>());
        }

        var externals = new Dictionary<string, string>();
        var indexRoot = new XElement("object");
        FcbClass rootClass = defs.GetClass(root.TypeHash);
        if (rootClass.Name is not null)
        {
            indexRoot.SetAttributeValue("type", rootClass.Name);
        }
        indexRoot.SetAttributeValue("hash", root.TypeHash.ToString("X8"));

        for (int i = 0; i < root.Children.Count; i++)
        {
            string fileName = ids[i];
            (XElement childEl, _) = WriteObject(root.Children[i], defs); // flat lookup - matches Gibbed's multi-export quirk
            externals[fileName] = Render(childEl);

            indexRoot.Add(new XElement("object", new XAttribute("external", fileName)));
        }

        return new FcbXmlExport(Render(indexRoot), externals);
    }

    /// <summary>
    /// The fragment ids <see cref="ToXml"/> would split <paramref name="root"/> into, without
    /// needing <see cref="FcbClassDefinitions"/> — the split decision and each child's <c>NN_Name</c>
    /// id only ever touch <see cref="FcbObject.TypeHash"/>/<see cref="FcbObject.Children"/>/raw
    /// <see cref="FcbObject.Values"/>, never the class/member config. Empty for a root that doesn't
    /// match the entity-library-of-groups shape.
    /// </summary>
    public static IReadOnlyList<string> ListFragmentIds(FcbObject root)
        => TryGetFragmentIds(root, out IReadOnlyList<string> ids) ? ids : [];

    /// <summary>
    /// Every fragment id <see cref="ToXml"/> would produce, paired with the byte length (UTF-8, no
    /// BOM — matching exactly what <see cref="ExtractFragment"/>'s callers actually return) of its
    /// rendered XML. Unlike <see cref="ListFragmentIds"/>, this needs <see cref="FcbClassDefinitions"/>
    /// and does the full per-child render, so it costs what <see cref="ToXml"/> costs — worth it once
    /// (and worth caching), not worth doing just to decide whether something splits.
    /// </summary>
    public static IReadOnlyList<FcbFragmentInfo> ListFragmentsWithSize(FcbObject root, FcbClassDefinitions defs)
    {
        if (!TryGetFragmentIds(root, out IReadOnlyList<string> ids))
        {
            return [];
        }

        var result = new List<FcbFragmentInfo>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            (XElement el, _) = WriteObject(root.Children[i], defs);
            long size = Encoding.UTF8.GetByteCount(Render(el));
            result.Add(new FcbFragmentInfo(ids[i], size));
        }
        return result;
    }

    /// <summary>
    /// Renders the single child of <paramref name="root"/> whose <see cref="ToXml"/>-assigned id is
    /// <paramref name="fragmentId"/>, or null if <paramref name="root"/> doesn't split or no child's
    /// id matches (e.g. the tree changed shape since the id was recorded). Case-insensitive, matching
    /// every other fragment id comparison in this codebase (<c>_fragmentOverrides</c>,
    /// <c>ModPathHashing</c>) — a staged override's id has already been lowercased by
    /// <c>NameHash.Normalize</c> by the time it reaches here, but this method's own ids come straight
    /// from the game data's real casing.
    /// </summary>
    public static string? ExtractFragment(FcbObject root, string fragmentId, FcbClassDefinitions defs)
    {
        if (!TryGetFragmentIds(root, out IReadOnlyList<string> ids))
        {
            return null;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], fragmentId, StringComparison.OrdinalIgnoreCase))
            {
                (XElement el, _) = WriteObject(root.Children[i], defs);
                return Render(el);
            }
        }
        return null;
    }

    /// <summary>
    /// Shared by <see cref="ToXml"/>, <see cref="ListFragmentIds"/> and <see cref="ExtractFragment"/>:
    /// whether <paramref name="root"/> matches the entity-library-of-groups shape, and if so, the
    /// <c>NN_Name.xml</c> id <see cref="ToXml"/> would assign each child, in <c>root.Children</c> order.
    /// </summary>
    private static bool TryGetFragmentIds(FcbObject root, out IReadOnlyList<string> ids)
    {
        bool isEntityLibraryOfGroups =
            root.Values.Count == 0
            && root.TypeHash == EntityLibraryTypeHash
            && root.Children.Count > 0
            && root.Children.All(c => c.TypeHash == EntityLibraryGroupTypeHash);

        if (!isEntityLibraryOfGroups)
        {
            ids = [];
            return false;
        }

        var computed = new List<string>(root.Children.Count);
        int counter = 0;
        int padLength = root.Children.Count.ToString(CultureInfo.InvariantCulture).Length;
        foreach (FcbObject child in root.Children)
        {
            counter++;
            string fileBaseName = counter.ToString(CultureInfo.InvariantCulture).PadLeft(padLength, '0');
            if (child.Values.TryGetValue(NameFieldHash, out byte[]? nameBytes) && TryDecodeCString(nameBytes, out string name))
            {
                fileBaseName += "_" + SanitizeFileNamePart(name);
            }
            computed.Add(fileBaseName + ".xml");
        }

        ids = computed;
        return true;
    }

    /// <summary>
    /// Reverse of <see cref="ToXml"/>. <paramref name="resolveExternal"/> is called with the plain
    /// filename from an <c>external="..."</c> attribute and must return that file's own XML text;
    /// required only if <paramref name="indexXml"/> actually contains such a reference.
    /// </summary>
    public static FcbObject FromXml(string indexXml, Func<string, string>? resolveExternal = null)
    {
        XElement root = XDocument.Parse(indexXml).Root
            ?? throw new InvalidDataException("Empty FCB XML document.");
        return LoadNode(root, resolveExternal);
    }

    /// <summary>
    /// Re-renders one fragment's XML through this class's own writer, so two texts that mean the same
    /// thing but came from different editors (attribute order, quoting, indentation, self-closing
    /// tags) compare equal before <see cref="Diff3.Merge"/> ever sees them — see docs/design/
    /// fcb-fragment-overlays.md's Milestone 3 "canonicalize before diffing" note. A genuine content
    /// change (e.g. a reordered <c>&lt;value&gt;</c>) still round-trips as a real difference, since
    /// <see cref="FcbObject.Values"/>/<see cref="FcbObject.Children"/> insertion order affects the
    /// actual rendered output.
    /// </summary>
    public static string CanonicalizeFragment(string fragmentXml, FcbClassDefinitions defs)
        => RenderObject(FromXml(fragmentXml), defs);

    /// <summary>
    /// Renders <paramref name="obj"/> as a single, un-split document - the same per-object writer
    /// <see cref="ToXml"/> uses for each piece of an entity-library split, exposed directly for a
    /// caller (JackAll.App's structured fragment editor) that already holds a decoded
    /// <see cref="FcbObject"/> from editing it natively rather than as text, and only needs XML as the
    /// serialization format for staging - not <see cref="ToXml"/>'s multi-export splitting behavior,
    /// which only makes sense for a whole container's root, never for one already-extracted fragment.
    /// </summary>
    public static string RenderObject(FcbObject obj, FcbClassDefinitions defs)
    {
        (XElement el, _) = WriteObject(obj, defs);
        return Render(el);
    }

    private static (XElement Element, FcbClass OwnClass) WriteObject(FcbObject obj, IFcbClassScope scope)
    {
        FcbClass ownClass = scope.Resolve(obj.TypeHash);
        var el = new XElement("object");
        if (ownClass.Name is not null)
        {
            el.SetAttributeValue("type", ownClass.Name);
        }
        else
        {
            el.SetAttributeValue("hash", obj.TypeHash.ToString("X8"));
        }

        foreach ((uint nameHash, byte[] value) in obj.Values)
        {
            WriteValueEntry(el, nameHash, ownClass.FindMember(nameHash), value);
        }

        foreach (FcbObject child in obj.Children)
        {
            (XElement childEl, _) = WriteObject(child, ownClass);
            el.Add(childEl);
        }

        return (el, ownClass);
    }

    private static void WriteValueEntry(XElement parent, uint nameHash, FcbMember? member, byte[] value)
    {
        var valueEl = new XElement("value");
        if (member?.Name is not null)
        {
            valueEl.SetAttributeValue("name", member.Name);
        }
        else
        {
            valueEl.SetAttributeValue("hash", nameHash.ToString("X8"));
        }

        FcbMemberType type = member?.Type ?? FcbMemberType.BinHex;

        // Every TryWriteValue case checks the byte length before writing anything, so a `false`
        // return here is guaranteed not to have left partial content on valueEl - safe to just
        // overwrite it as BinHex (e.g. the config says Float32 but this particular value is the
        // wrong length; happens if a hand-edit or an unusual file disagrees with the config).
        if (type == FcbMemberType.BinHex || !TryWriteValue(valueEl, type, value))
        {
            valueEl.SetAttributeValue("type", nameof(FcbMemberType.BinHex));
            valueEl.Value = Convert.ToHexString(value);
        }
        else
        {
            valueEl.SetAttributeValue("type", type.ToString());
        }

        parent.Add(valueEl);
    }

    /// <summary>
    /// Writes one value's content into <paramref name="el"/> for the given declared <paramref name="type"/>
    /// - the same byte-to-XML encoding <see cref="RenderObject"/> itself uses, exposed directly so a
    /// caller with its own (unverified, class-resolution-free) name/type source - JackAll.App's savegame
    /// renderer, specifically - can still produce byte-for-byte the same shape a real resolved .fcb
    /// member would, rather than re-implementing this switch a second time. Returns <see langword="false"/>
    /// without modifying <paramref name="el"/> if <paramref name="value"/>'s length doesn't match what
    /// <paramref name="type"/> requires.
    /// </summary>
    public static bool TryWriteValue(XElement el, FcbMemberType type, byte[] value)
    {
        switch (type)
        {
            case FcbMemberType.Hash:
                if (value.Length != 4) return false;
                el.Value = BitConverter.ToUInt32(value, 0).ToString("X8", CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.String:
                if (value.Length < 1 || value[^1] != 0) return false;
                el.Value = Encoding.UTF8.GetString(value, 0, value.Length - 1);
                return true;

            case FcbMemberType.Enum:
                if (value.Length != 4) return false;
                el.Value = BitConverter.ToUInt32(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.Bool:
                if (value.Length != 1) return false;
                el.Value = (value[0] != 0).ToString();
                return true;

            case FcbMemberType.Float:
                if (value.Length != 4) return false;
                el.Value = BitConverter.ToSingle(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.Int32:
                if (value.Length != 4) return false;
                el.Value = BitConverter.ToInt32(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.UInt32:
                if (value.Length != 4) return false;
                el.Value = BitConverter.ToUInt32(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.Int64:
                if (value.Length != 8) return false;
                el.Value = BitConverter.ToInt64(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.UInt64:
                if (value.Length != 8) return false;
                el.Value = BitConverter.ToUInt64(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.Vector2:
                if (value.Length != 4 * 2) return false;
                el.Add(new XElement("x", Single(value, 0)), new XElement("y", Single(value, 4)));
                return true;

            case FcbMemberType.Vector3:
                if (value.Length != 4 * 3) return false;
                el.Add(new XElement("x", Single(value, 0)), new XElement("y", Single(value, 4)), new XElement("z", Single(value, 8)));
                return true;

            case FcbMemberType.Vector4:
                if (value.Length != 4 * 4) return false;
                el.Add(new XElement("x", Single(value, 0)), new XElement("y", Single(value, 4)),
                       new XElement("z", Single(value, 8)), new XElement("w", Single(value, 12)));
                return true;

            case FcbMemberType.UInt32Array:
                return TryWriteFixedArray(el, value, 4, (v, o) => BitConverter.ToUInt32(v, o).ToString(CultureInfo.InvariantCulture));

            case FcbMemberType.HashArray:
                return TryWriteFixedArray(el, value, 4, (v, o) => BitConverter.ToUInt32(v, o).ToString("X8", CultureInfo.InvariantCulture));

            case FcbMemberType.Int32Array:
                return TryWriteFixedArray(el, value, 4, (v, o) => BitConverter.ToInt32(v, o).ToString(CultureInfo.InvariantCulture));

            case FcbMemberType.FloatArray:
                return TryWriteFixedArray(el, value, 4, Single);

            case FcbMemberType.Bool32Array:
                return TryWriteFixedArray(el, value, 4, (v, o) => (BitConverter.ToUInt32(v, o) != 0).ToString());

            case FcbMemberType.Vector3Array:
                return TryWriteVector3Array(el, value);

            case FcbMemberType.Int8:
                if (value.Length != 1) return false;
                el.Value = ((sbyte)value[0]).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.UInt8:
                if (value.Length != 1) return false;
                el.Value = value[0].ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.Int16:
                if (value.Length != 2) return false;
                el.Value = BitConverter.ToInt16(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.UInt16:
                if (value.Length != 2) return false;
                el.Value = BitConverter.ToUInt16(value, 0).ToString(CultureInfo.InvariantCulture);
                return true;

            case FcbMemberType.Bool16:
                if (value.Length != 2) return false;
                el.Value = (BitConverter.ToUInt16(value, 0) != 0).ToString();
                return true;

            case FcbMemberType.Bool32:
                if (value.Length != 4) return false;
                el.Value = (BitConverter.ToUInt32(value, 0) != 0).ToString();
                return true;

            case FcbMemberType.Matrix4:
                if (value.Length != 4 * 16) return false;
                for (int row = 0; row < 4; row++)
                {
                    int o = row * 16;
                    el.Add(new XElement("row",
                        new XElement("x", Single(value, o)), new XElement("y", Single(value, o + 4)),
                        new XElement("z", Single(value, o + 8)), new XElement("w", Single(value, o + 12))));
                }
                return true;

            case FcbMemberType.Rml:
                if (TryDecodeRml(value, out XElement? decoded))
                {
                    el.Add(decoded);
                }
                else
                {
                    // Not a well-formed .rml payload - same opaque-hex fallback as before (see class
                    // remarks), not BinHex-shaped so ReadValue can tell "always was hex" apart from
                    // "decoded to nested XML" on the way back in.
                    el.Value = Convert.ToHexString(value);
                }
                return true;

            default:
                return false;
        }
    }

    /// <summary>Writes a count-prefixed array of fixed-size scalar items (UInt32Array, HashArray, ...).</summary>
    private static bool TryWriteFixedArray(XElement el, byte[] value, int itemSize, Func<byte[], int, string> format)
    {
        if (value.Length < 4) return false;
        int count = BitConverter.ToInt32(value, 0);
        if (count < 0 || value.Length != 4 + (count * itemSize)) return false;

        for (int i = 0, offset = 4; i < count; i++, offset += itemSize)
        {
            el.Add(new XElement("item", format(value, offset)));
        }
        return true;
    }

    /// <summary>Vector3Array's items aren't scalars, so it gets its own writer instead of using <see cref="TryWriteFixedArray"/>.</summary>
    private static bool TryWriteVector3Array(XElement el, byte[] value)
    {
        const int itemSize = 4 * 3;
        if (value.Length < 4) return false;
        int count = BitConverter.ToInt32(value, 0);
        if (count < 0 || value.Length != 4 + (count * itemSize)) return false;

        for (int i = 0, offset = 4; i < count; i++, offset += itemSize)
        {
            el.Add(new XElement("item",
                new XElement("x", Single(value, offset)),
                new XElement("y", Single(value, offset + 4)),
                new XElement("z", Single(value, offset + 8))));
        }
        return true;
    }

    private static string Single(byte[] value, int offset)
        => BitConverter.ToSingle(value, offset).ToString(CultureInfo.InvariantCulture);

    private static FcbObject LoadNode(XElement node, Func<string, string>? resolveExternal)
    {
        string? external = (string?)node.Attribute("external");
        if (!string.IsNullOrEmpty(external))
        {
            if (resolveExternal is null)
            {
                throw new InvalidDataException(
                    $"'{external}' is an external reference, but no resolver was supplied to FromXml.");
            }

            XElement externalRoot = XDocument.Parse(resolveExternal(external)).Root
                ?? throw new InvalidDataException($"'{external}' has no root element.");
            return LoadNode(externalRoot, resolveExternal); // an external file can itself reference further externals
        }

        return ReadNode(node, resolveExternal);
    }

    private static FcbObject ReadNode(XElement node, Func<string, string>? resolveExternal)
    {
        var obj = new FcbObject { TypeHash = LoadNameOrHash(node, "type") };

        foreach (XElement valueEl in node.Elements("value"))
        {
            uint nameHash = LoadNameOrHash(valueEl, "name");
            string typeText = (string?)valueEl.Attribute("type")
                ?? throw new InvalidDataException("A <value> element is missing its 'type' attribute.");
            if (!Enum.TryParse(typeText, out FcbMemberType type))
            {
                throw new InvalidDataException($"Unknown FCB value type '{typeText}'.");
            }

            obj.Values[nameHash] = ReadValue(valueEl, type);
        }

        foreach (XElement childEl in node.Elements("object"))
        {
            obj.Children.Add(LoadNode(childEl, resolveExternal));
        }

        return obj;
    }

    private static byte[] ReadValue(XElement el, FcbMemberType type) => type switch
    {
        FcbMemberType.BinHex => Convert.FromHexString(el.Value.Trim()),
        FcbMemberType.Rml => ReadRml(el),
        FcbMemberType.Hash => BitConverter.GetBytes(uint.Parse(el.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
        FcbMemberType.String => NullTerminate(Encoding.UTF8.GetBytes(el.Value)),
        FcbMemberType.Enum => BitConverter.GetBytes(uint.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.Bool => [(byte)(bool.Parse(el.Value) ? 1 : 0)],
        FcbMemberType.Float => BitConverter.GetBytes(float.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.Int32 => BitConverter.GetBytes(int.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.UInt32 => BitConverter.GetBytes(uint.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.Int64 => BitConverter.GetBytes(long.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.UInt64 => BitConverter.GetBytes(ulong.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.Vector2 => [.. Float(el, "x"), .. Float(el, "y")],
        FcbMemberType.Vector3 => [.. Float(el, "x"), .. Float(el, "y"), .. Float(el, "z")],
        FcbMemberType.Vector4 => [.. Float(el, "x"), .. Float(el, "y"), .. Float(el, "z"), .. Float(el, "w")],
        FcbMemberType.UInt32Array => ReadFixedArray(el, 4, e => BitConverter.GetBytes(uint.Parse(e.Value, CultureInfo.InvariantCulture))),
        FcbMemberType.HashArray => ReadFixedArray(el, 4, e => BitConverter.GetBytes(uint.Parse(e.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))),
        FcbMemberType.Int32Array => ReadFixedArray(el, 4, e => BitConverter.GetBytes(int.Parse(e.Value, CultureInfo.InvariantCulture))),
        FcbMemberType.FloatArray => ReadFixedArray(el, 4, e => BitConverter.GetBytes(float.Parse(e.Value, CultureInfo.InvariantCulture))),
        FcbMemberType.Bool32Array => ReadFixedArray(el, 4, e => BitConverter.GetBytes((uint)(bool.Parse(e.Value) ? 1 : 0))),
        FcbMemberType.Vector3Array => ReadFixedArray(el, 4 * 3, e => [.. Float(e, "x"), .. Float(e, "y"), .. Float(e, "z")]),
        FcbMemberType.Int8 => [(byte)sbyte.Parse(el.Value, CultureInfo.InvariantCulture)],
        FcbMemberType.UInt8 => [byte.Parse(el.Value, CultureInfo.InvariantCulture)],
        FcbMemberType.Int16 => BitConverter.GetBytes(short.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.UInt16 => BitConverter.GetBytes(ushort.Parse(el.Value, CultureInfo.InvariantCulture)),
        FcbMemberType.Bool16 => BitConverter.GetBytes((ushort)(bool.Parse(el.Value) ? 1 : 0)),
        FcbMemberType.Bool32 => BitConverter.GetBytes((uint)(bool.Parse(el.Value) ? 1 : 0)),
        FcbMemberType.Matrix4 => ReadMatrix4(el),
        _ => throw new InvalidDataException($"Unsupported FCB value type '{type}'."),
    };

    /// <summary>
    /// Tries to parse <paramref name="value"/> as a nested .rml document - see
    /// <see cref="RmlDocument"/>.
    /// </summary>
    /// <remarks>
    /// An FCB Rml-typed value's bytes are normally the .rml document plus one trailing 0x00 pad byte -
    /// a container-level convention distinct from the .rml format itself (confirmed against every one
    /// of the 2,328 real Rml-typed values across the game's 4 shipped entitylibrary trees: every single
    /// one is exactly <c>RmlDocument.Serialize(...)</c>'s bytes with one extra trailing zero, never zero
    /// or two - i.e. this is the base game's own convention, not a guess). <see cref="RmlDocument"/>
    /// itself knows nothing about this pad byte - it's stripped here and always re-added by
    /// <see cref="ReadRml"/>, the same way this class already NUL-terminates
    /// <see cref="FcbMemberType.String"/> at the FCB layer rather than inside a shared string codec.
    ///
    /// A third-party modding tool's own (re-implemented, sometimes non-conforming) FCB writer can
    /// produce a value that skips this pad byte - nothing in the format documents or enforces it - so
    /// the padded shape is tried first (the base game's own convention), and only on failure is the
    /// bare, unpadded shape tried too, purely so more of what's actually out there is readable as XML
    /// here instead of falling back to opaque hex. This is a one-way accommodation for reading only:
    /// <see cref="ReadRml"/> always writes the padded shape back out regardless of which one a given
    /// value decoded from, since a value re-imported through this class should end up looking like
    /// something the base game could have shipped, not preserve whatever a modding tool's writer did.
    ///
    /// Either shape also requires the decode to be lossless (re-encoding reproduces the shape-specific
    /// byte span exactly) before being accepted, not just that it parses - consistent with this tool's
    /// core guarantee that an unedited value round-trips byte-for-byte (see the README's "building
    /// twice produces identical bytes") for the padded shape, which is the one every real sample uses.
    /// A value that satisfies neither shape (hand-edited bytes, a future engine version, an unseen
    /// shape) falls back to the same opaque hex as an unparseable one rather than silently corrupting
    /// it.
    ///
    /// Goes through <see cref="RmlDocument.TryDeserialize"/>, not the throwing <see cref="RmlDocument.Deserialize"/>
    /// - "doesn't parse" is the expected outcome for one of the two shapes on every value that lacks
    /// the FCB-layer pad byte, so a whole .fcb's worth of Rml values (thousands, for a big entity
    /// library) would otherwise mean thousands of caught exceptions just to find the shape that works.
    /// </remarks>
    private static bool TryDecodeRml(byte[] value, out XElement? element)
        => TryDecodeRmlShape(value, stripPadByte: true, out element)
        || TryDecodeRmlShape(value, stripPadByte: false, out element);

    /// <summary>Public entry point onto <see cref="TryDecodeRml"/> for callers that want the decoded
    /// .rml document itself rather than embedded in this class's own <c>&lt;value type="Rml"&gt;</c>
    /// text wrapper - currently the interactive property grid's Rml field (see
    /// JackAll.App.XmlEditor.FcbFieldFormat), which shows/edits it as a plain XML string instead of
    /// opaque hex. Null for the opaque-hex fallback shape (see <see cref="TryDecodeRml"/>'s remarks).</summary>
    public static XElement? TryDecodeRmlValue(byte[] value) => TryDecodeRml(value, out XElement? element) ? element : null;

    /// <summary>Reverse of <see cref="TryDecodeRmlValue"/>: encodes an .rml document back to a
    /// Rml-typed value's raw bytes, always in the base game's padded shape (see
    /// <see cref="TryDecodeRml"/>'s remarks) - shared with <see cref="ReadRml"/>'s own nested-element
    /// branch so the two paths can't drift apart.</summary>
    public static byte[] EncodeRmlValue(XElement root)
    {
        byte[] rml = RmlDocument.Serialize(root);
        byte[] value = new byte[rml.Length + 1]; // trailing byte is already 0 from the allocation
        rml.CopyTo(value, 0);
        return value;
    }

    private static bool TryDecodeRmlShape(byte[] value, bool stripPadByte, out XElement? element)
    {
        if (stripPadByte && (value.Length < 1 || value[^1] != 0))
        {
            element = null;
            return false;
        }

        byte[] rml = stripPadByte ? value[..^1] : value;
        if (!RmlDocument.TryDeserialize(rml, out XElement? candidate)
            || !RmlDocument.Serialize(candidate).AsSpan().SequenceEqual(rml))
        {
            element = null;
            return false;
        }

        element = candidate;
        return true;
    }

    /// <summary>Reverse of the <see cref="TryDecodeRml"/> branch in <see cref="TryWriteValue"/>: a
    /// <c>&lt;value type="Rml"&gt;</c> either wraps one decoded root element (re-encode it and add the
    /// base game's trailing pad byte - unconditionally, even if the source value didn't have one; see
    /// <see cref="TryDecodeRml"/>'s remarks) or, for the opaque-hex fallback, has none (parse its text
    /// as hex, same as BinHex).</summary>
    private static byte[] ReadRml(XElement el)
    {
        XElement? nested = el.Elements().FirstOrDefault();
        return nested is null ? Convert.FromHexString(el.Value.Trim()) : EncodeRmlValue(nested);
    }

    private static byte[] Float(XElement el, string childName)
    {
        XElement child = el.Element(childName)
            ?? throw new InvalidDataException($"Vector value is missing its '{childName}' element.");
        return BitConverter.GetBytes(float.Parse(child.Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Reads a count-prefixed array of fixed-size items, mirroring <see cref="TryWriteFixedArray"/>.</summary>
    private static byte[] ReadFixedArray(XElement el, int itemSize, Func<XElement, byte[]> parseItem)
    {
        List<byte[]> items = [.. el.Elements("item").Select(parseItem)];

        byte[] result = new byte[4 + (items.Count * itemSize)];
        BitConverter.GetBytes(items.Count).CopyTo(result, 0);
        for (int i = 0; i < items.Count; i++)
        {
            items[i].CopyTo(result, 4 + (i * itemSize));
        }
        return result;
    }

    private static byte[] ReadMatrix4(XElement el)
    {
        XElement[] rows = [.. el.Elements("row")];
        if (rows.Length != 4)
        {
            throw new InvalidDataException($"Matrix4 value needs exactly 4 <row> elements, found {rows.Length}.");
        }

        byte[] result = new byte[4 * 16];
        for (int r = 0; r < 4; r++)
        {
            byte[] row = [.. Float(rows[r], "x"), .. Float(rows[r], "y"), .. Float(rows[r], "z"), .. Float(rows[r], "w")];
            row.CopyTo(result, r * 16);
        }
        return result;
    }

    private static uint LoadNameOrHash(XElement node, string nameAttribute)
    {
        string? name = (string?)node.Attribute(nameAttribute);
        string? hash = (string?)node.Attribute("hash");

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(hash))
        {
            throw new InvalidDataException($"<{node.Name}> needs a '{nameAttribute}' or 'hash' attribute.");
        }

        return !string.IsNullOrWhiteSpace(name)
            ? FcbClassDefinitions.Crc32Ascii(name)
            : uint.Parse(hash!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static byte[] NullTerminate(byte[] utf8)
    {
        byte[] result = new byte[utf8.Length + 1]; // trailing byte is already 0 from the allocation
        utf8.CopyTo(result, 0);
        return result;
    }

    private static bool TryDecodeCString(byte[] value, out string text)
    {
        if (value.Length < 1 || value[^1] != 0)
        {
            text = "";
            return false;
        }
        text = Encoding.UTF8.GetString(value, 0, value.Length - 1);
        return text.Length > 0;
    }

    private static string SanitizeFileNamePart(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }

    private static string Render(XElement element)
    {
        var settings = new XDocument(element);
        return settings.ToString();
    }
}
