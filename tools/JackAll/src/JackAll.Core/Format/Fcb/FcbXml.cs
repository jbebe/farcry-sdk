using System.Globalization;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Format.Fcb;

/// <summary>One fragment's <see cref="FcbFragments"/>-assigned id, paired with its binary size —
/// see <see cref="FcbXml.ListFragmentsWithSize"/>.</summary>
public readonly record struct FcbFragmentInfo(string Id, long Size);

/// <summary>
/// Converts between a parsed <see cref="FcbObject"/> tree and Gibbed-compatible XML — same element
/// shape (`&lt;object type="..."|hash="..."&gt;`, `&lt;value name="..."|hash="..." type="..."&gt;`),
/// and the same value-type encodings.
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
    /// <summary>
    /// Converts a parsed FCB tree to one XML document — a whole container's root or a single
    /// fragment's node alike.
    /// </summary>
    public static string ToXml(FcbObject obj, FcbClassDefinitions defs)
    {
        (XElement el, _) = WriteObject(obj, defs);
        return Render(el);
    }

    /// <summary>
    /// Every override-unit id of <paramref name="root"/> (see <see cref="FcbFragments"/> for the
    /// recognised shapes and id scheme) with its <see cref="FcbDocument.EncodedSize"/> — the fully
    /// expanded byte size, not the (possibly backreference-deduplicated) span it occupied in the file
    /// it came from: a nested node's on-disk span isn't tracked by the decoder, and this is a display
    /// number for the file browser's size column, nothing more (see <c>GameVfs</c>).
    /// </summary>
    public static IReadOnlyList<FcbFragmentInfo> ListFragmentsWithSize(FcbObject root)
        => [.. FcbFragments.Slots(root)
            .Select(s => new FcbFragmentInfo(s.Id, FcbDocument.EncodedSize(s.Node)))];

    /// <summary>
    /// Renders the node of <paramref name="root"/> whose <see cref="FcbFragments"/> id is
    /// <paramref name="fragmentId"/>, or null if <paramref name="root"/> doesn't split or nothing
    /// matches (e.g. the tree changed shape since the id was recorded). Matching runs through
    /// <see cref="FcbFragments.IdComparer"/> — a staged override's id arrives lowercased by
    /// <c>NameHash.Normalize</c>, while the tree's own ids carry the game data's real casing.
    /// </summary>
    public static string? ExtractFragment(FcbObject root, string fragmentId, FcbClassDefinitions defs)
        => FcbFragments.Find(root, fragmentId) is { } node ? ToXml(node, defs) : null;

    /// <summary>
    /// <paramref name="root"/> with every fragment replaced by a marker carrying its canonical id,
    /// and fragments <paramref name="keep"/> rejects dropped outright — see
    /// <c>IContainerTree.Skeleton</c>.
    /// </summary>
    public static string SkeletonXml(
        FcbObject root, IReadOnlyList<FcbFragment> fragments, Func<string, bool> keep, FcbClassDefinitions defs)
    {
        var markerById = new Dictionary<FcbObject, string?>(ReferenceEqualityComparer.Instance);

        // A declaration a later one supersedes is not part of the shape: the engine's map replaces on
        // collision, so nothing can name it and nothing loads it. Leaving it in would make a mod that
        // shipped one impossible to reproduce from fragments, for content the game never reads.
        foreach (FcbFragment shadowed in FcbFragments.Shadowed(root))
        {
            markerById[shadowed.Node] = null;
        }

        foreach (FcbFragment fragment in fragments)
        {
            markerById[fragment.Node] = keep(fragment.Id) ? FcbFragments.Canonicalize(fragment.Id) : null;
        }

        FcbObject skeleton = Skeleton(root, markerById);

        // A library's groups are ordered by name rather than compared where they sit: a mod appends
        // the groups it adds in its own arbitrary order, and an override set that rebuilds the same
        // groups with the same archetypes has rebuilt the container. Nothing else is reordered - a
        // group's own archetypes stay in document order, which last-wins resolution depends on.
        if (FcbFragments.IsLibraryOfGroups(root))
        {
            List<FcbObject> byName = [.. skeleton.Children.OrderBy(
                g => FcbEntityFields.ReadString(g, WorldHashes.Name), StringComparer.OrdinalIgnoreCase)];
            skeleton.Children.Clear();
            skeleton.Children.AddRange(byName);
        }

        return ToXml(skeleton, defs);
    }

    private static FcbObject Skeleton(FcbObject node, Dictionary<FcbObject, string?> markerById)
    {
        var clone = new FcbObject { TypeHash = node.TypeHash };
        foreach ((uint nameHash, byte[] value) in node.Values)
        {
            clone.Values.Add(nameHash, value);
        }

        foreach (FcbObject child in node.Children)
        {
            if (markerById.TryGetValue(child, out string? canonicalId))
            {
                if (canonicalId is not null)
                {
                    var marker = new FcbObject { TypeHash = 0 };
                    marker.Values.Add(0, Encoding.UTF8.GetBytes(canonicalId));
                    clone.Children.Add(marker);
                }
                continue;
            }

            clone.Children.Add(Skeleton(child, markerById));
        }

        return clone;
    }

    /// <summary>Reverse of <see cref="ToXml"/>.</summary>
    public static FcbObject FromXml(string xml)
    {
        XElement root = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("Empty FCB XML document.");
        return ReadNode(root);
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
        => ToXml(FromXml(fragmentXml), defs);

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
    /// - the same byte-to-XML encoding <see cref="ToXml"/> itself uses, exposed directly so a
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
                el.Value = Single(value, 0);
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
        if (!FcbWire.TryReadFixedArray(value, itemSize, (v, o) => new XElement("item", format(v, o)), out XElement[] items))
        {
            return false;
        }
        el.Add(items);
        return true;
    }

    /// <summary>Vector3Array's items aren't scalars, so it gets its own writer instead of using <see cref="TryWriteFixedArray"/>.</summary>
    private static bool TryWriteVector3Array(XElement el, byte[] value)
    {
        if (!FcbWire.TryReadFixedArray(value, 4 * 3, (v, o) => new XElement("item",
                new XElement("x", Single(v, o)),
                new XElement("y", Single(v, o + 4)),
                new XElement("z", Single(v, o + 8))), out XElement[] items))
        {
            return false;
        }
        el.Add(items);
        return true;
    }

    /// <summary>
    /// Renders one float, with negative zero as "0" - the form Gibbed's tools write - rather than
    /// .NET's "-0". Their .NET Framework 7-digit rounding is deliberately not matched: it would emit
    /// text that no longer parses back to the same float.
    /// </summary>
    private static string Single(byte[] value, int offset)
    {
        float single = BitConverter.ToSingle(value, offset);
        return single == 0f ? "0" : single.ToString(CultureInfo.InvariantCulture);
    }

    private static FcbObject ReadNode(XElement node)
    {
        if (node.Attribute("external") is { } external)
        {
            throw new InvalidDataException(
                $"'{external.Value}' is an external reference; multi-file FCB XML is no longer supported.");
        }

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
            obj.Children.Add(ReadNode(childEl));
        }

        return obj;
    }

    private static byte[] ReadValue(XElement el, FcbMemberType type) => type switch
    {
        FcbMemberType.BinHex => Convert.FromHexString(el.Value.Trim()),
        FcbMemberType.Rml => ReadRml(el),
        FcbMemberType.Hash => BitConverter.GetBytes(uint.Parse(el.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
        FcbMemberType.String => FcbWire.NullTerminate(Encoding.UTF8.GetBytes(el.Value)),
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
    /// JackAll.Tools.Fcb.FcbFieldFormat), which shows/edits it as a plain XML string instead of
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
        byte[][] items = [.. el.Elements("item").Select(parseItem)];
        return FcbWire.WriteFixedArray(items, itemSize, (buf, offset, item) => item.CopyTo(buf, offset));
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

    private static string Render(XElement element)
    {
        var settings = new XDocument(element);
        return settings.ToString();
    }
}
