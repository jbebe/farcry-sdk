namespace JackAll.Core.Format;

/// <summary>
/// Decodes the body of a .mgb (Magma UI binary) file - everything past <see cref="MgbHeader"/> - into
/// a generic <see cref="MgbNode"/> tree. Implements the field sequences documented in
/// reverse/dunia/mgb_format.md ("VisitPackage - full byte-exact preamble" and "Widget/record body").
/// </summary>
/// <remarks>
/// Two things in the source spec are inferred, not confirmed by decompilation, and are the most
/// likely place a real file will fail to parse:
/// <list type="bullet">
/// <item>The <c>LoadMaterial</c>/<c>LoadFontFamily</c> helper calls (used by <c>AreaInstance</c> and
/// its forwarders, <c>Image</c>, and <c>Text</c>) were never traced to their own byte consumption in
/// the reverse-engineering pass - only that they get called. This decoder assumes they read a
/// length-prefixed ANSI string, matching the pattern used identically everywhere else a resource is
/// referenced in this format (materials' own texture name, fonts' file paths, the package's default
/// material) - a well-motivated guess, not a confirmed one.</item>
/// <item>Which concrete <c>*State</c> type a <c>Keyframe</c>'s nested state uses isn't determined by a
/// byte in the stream (see the doc) - this decoder infers it from the owning element's own class name
/// (<c>RectShape</c> → <c>RectShapeState</c>, etc.), which covers the content-specific state types but
/// not the generic transform states (<c>PosState</c>/<c>RotationState</c>/<c>ScaleState</c>/
/// <c>RectState</c>), whose actual selection mechanism is still unknown.</item>
/// </list>
/// Both failure modes throw a clear <see cref="NotSupportedException"/>/<see cref="InvalidDataException"/>
/// naming exactly what wasn't understood and at what byte offset, rather than silently guessing further.
/// The <c>ActionExecuter</c> family (attached via <c>ActionCaller</c> to any <c>Area</c>/<c>Element</c>/
/// <c>Keyframe</c>) isn't decoded at all yet - it's a different subsystem from widget geometry, out of
/// scope for this decoder for now (see the doc's "Not yet traced").
/// </remarks>
public static class MgbBody
{
    public static MgbNode ParsePackage(byte[] data, MgbHeader header)
    {
        var reader = new MgbReader(data, header.HeaderLength);
        var fields = new List<MgbField>();
        var children = new List<MgbNode>();

        // 1. Fixed 260-byte config block: 65 raw 32-bit values, not individually decoded (see the
        // doc - each one is forwarded to a distinct Package setter, but their individual meaning
        // wasn't traced).
        for (int i = 0; i < 65; i++)
        {
            reader.ReadValue();
        }
        fields.Add(new MgbField("ConfigBlock", "65 raw 32-bit values (not individually decoded)"));

        // 2. Package's own generic UserData property list.
        children.Add(ParseUserData(reader, header));

        // 3-4. PAGESIZE / DISPLAYOFFSET.
        ushort pageWidth = reader.ReadU16();
        ushort pageHeight = reader.ReadU16();
        fields.Add(new MgbField("PageSize", $"{pageWidth} x {pageHeight}"));
        ushort dispX = reader.ReadU16();
        ushort dispY = reader.ReadU16();
        fields.Add(new MgbField("DisplayOffset", $"({dispX}, {dispY})"));

        // 5. Materials.
        uint materialCount = reader.ReadInt();
        uint materialUnknownField = reader.ReadInt();
        fields.Add(new MgbField("MaterialUnknownField", $"0x{materialUnknownField:X8}"));
        var materials = new List<MgbNode>();
        for (uint i = 0; i < materialCount; i++)
        {
            materials.Add(ParseMaterial(reader));
        }
        children.Add(new MgbNode("Materials", [], materials));

        // 6. Font substitutions.
        uint fontSubstCount = reader.ReadInt();
        var fontSubsts = new List<MgbNode>();
        for (uint i = 0; i < fontSubstCount; i++)
        {
            fontSubsts.Add(ParseFontEntry(reader, header, "FontSubst"));
        }
        children.Add(new MgbNode("FontSubstitutions", [], fontSubsts));

        // 7. Font declarations.
        uint fontDeclCount = reader.ReadInt();
        var fontDecls = new List<MgbNode>();
        for (uint i = 0; i < fontDeclCount; i++)
        {
            fontDecls.Add(ParseFontEntry(reader, header, "FontDecl"));
        }
        children.Add(new MgbNode("FontDeclarations", [], fontDecls));

        // 8. Font families.
        uint fontFamilyCount = reader.ReadInt();
        var fontFamilies = new List<MgbNode>();
        for (uint i = 0; i < fontFamilyCount; i++)
        {
            fontFamilies.Add(ParseFontFamily(reader));
        }
        children.Add(new MgbNode("FontFamilies", [], fontFamilies));

        // 9. Top-level areas.
        uint areaCount = reader.ReadInt();
        // Once one area fails to decode, the reader's position can no longer be trusted for anything
        // after it (we don't know how many bytes the failed element would have consumed) - so this
        // stops rather than guessing at further areas or the trailing optional/default-material
        // fields. Whatever parsed successfully before the failure is still returned: a partial,
        // honest result is far more useful here than an all-or-nothing exception, especially while
        // MgbTypeTable's class-name coverage is incomplete (see reverse/dunia/mgb_format.md).
        var areas = new List<MgbNode>();
        for (uint i = 0; i < areaCount; i++)
        {
            try
            {
                areas.Add(ParseTypedElement(reader, header));
            }
            catch (Exception ex)
            {
                fields.Add(new MgbField("StoppedDecoding", $"after area {i}/{areaCount}: {ex.Message}"));
                children.Add(new MgbNode("Areas", [], areas));
                fields.Add(new MgbField("BytesConsumed", $"{reader.Position - header.HeaderLength:N0} (file has {data.Length - header.HeaderLength:N0} body bytes total)"));
                return new MgbNode("Package", fields, children);
            }
        }
        children.Add(new MgbNode("Areas", [], areas));

        // 10. Optional named special areas (not counted in areaCount).
        if (reader.ReadBool())
        {
            children.Add(new MgbNode("GlobalFocusArea", [], [ParseTypedElement(reader, header)]));
        }
        if (reader.ReadBool())
        {
            children.Add(new MgbNode("SecondArea", [], [ParseTypedElement(reader, header)]));
        }

        // 11. Default material.
        uint defaultMatLen = reader.ReadInt();
        if (defaultMatLen != 0)
        {
            fields.Add(new MgbField("DefaultMaterial", MgbReader.DecodeAnsi(reader.ReadBytes((int)defaultMatLen))));
        }

        fields.Add(new MgbField("BytesConsumed", $"{reader.Position - header.HeaderLength:N0} (file has {data.Length - header.HeaderLength:N0} body bytes total)"));

        return new MgbNode("Package", fields, children);
    }

    // --- Type dispatch -------------------------------------------------

    /// <summary>Reads a type-id byte (an index into the file's own header type table), resolves it to
    /// a class name, and parses that element. Used everywhere an Area's child list, or the package's
    /// own top-level area list, names a typed sub-object.</summary>
    private static MgbNode ParseTypedElement(MgbReader reader, MgbHeader header)
    {
        int typeIdOffset = reader.Position;
        byte typeIndex = reader.ReadByte();

        // RawId == 0 is the header spec's documented "left unresolved/skipped" case - a real, legal
        // empty slot in the file's own type table (not a class we're just missing a name for). An
        // element referencing it is a genuine null/empty placeholder: no further bytes to read.
        if (typeIndex < header.Types.Count && header.Types[typeIndex].RawId == 0)
        {
            return MgbNode.Leaf("(empty)");
        }

        string className = ResolveTypeOrThrow(header, typeIndex, typeIdOffset);
        return ParseElement(reader, header, className);
    }

    private static string ResolveTypeOrThrow(MgbHeader header, byte typeIndex, int atOffset)
    {
        if (typeIndex >= header.Types.Count)
        {
            throw new InvalidDataException($"Type index {typeIndex} at offset 0x{atOffset:X} is out of range (file only declares {header.Types.Count} types).");
        }
        MgbTypeEntry entry = header.Types[typeIndex];
        return entry.Name ?? throw new InvalidDataException(
            $"Type index {typeIndex} at offset 0x{atOffset:X} resolves to an unrecognized class (crc32=0x{entry.RawId:X8}) - " +
            "MgbTypeTable doesn't have a name for it yet, so this element's field layout is unknown.");
    }

    /// <summary>A type-index resolution that's purely a display label and never gates further byte
    /// reads (e.g. a font's type-id - the recursion it would trigger is a confirmed zero-byte no-op
    /// regardless of which class it resolves to) - so unlike <see cref="ResolveTypeOrThrow"/>, this
    /// never throws.</summary>
    private static string DescribeType(MgbHeader header, byte typeIndex)
    {
        if (typeIndex >= header.Types.Count)
        {
            return $"(index {typeIndex} out of range)";
        }
        MgbTypeEntry entry = header.Types[typeIndex];
        if (entry.Name is not null) return entry.Name;
        return entry.RawId == 0 ? "(empty slot)" : $"(unrecognized, crc32=0x{entry.RawId:X8})";
    }

    private static MgbNode ParseElement(MgbReader reader, MgbHeader header, string className) => className switch
    {
        "Area" => ParseArea(reader, header, "Area"),
        "Page" => ParsePage(reader, header),
        "CheckBox" => ParseAreaFixedFloats(reader, header, "CheckBox", 12),
        "Button" => ParseAreaFixedFloats(reader, header, "Button", 6),
        "Cursor" => ParseCursor(reader, header),
        "Element" => ParseBareElement(reader, header),
        // Unconfirmed: treated as a bare Element (no fields of its own) since it plays the same
        // "minimal root placeholder" role - the very first top-level area in several real files that
        // don't use bare "Element" for that slot. If real files desync shortly after this, that
        // hypothesis is wrong and needs revisiting (see reverse/dunia/mgb_format.md).
        "AnonymousType" => ParseBareElement(reader, header) with { Kind = "AnonymousType" },
        "RectShape" => ParseRectShape(reader, header),
        "TextBase" => ParseTextBase(reader, header, "TextBase"),
        "Text" => ParseText(reader, header),
        "Image" => ParseImage(reader, header),
        "ListBox" => ParseListBox(reader, header),
        "Window" => ParseWindow(reader, header),
        "Slider" => ParseSlider(reader, header),
        "EditBox" => ParseEditBox(reader, header),
        "Focusable" => ParseFocusable(reader, header),
        "Placeholder" or "AreaLinkTags" => MgbNode.Leaf(className), // confirmed no-ops, 0 bytes
        "AreaInstance" => ParseAreaInstance(reader, header, "AreaInstance"),
        "AutonomousAreaInstance" => ParseAreaInstance(reader, header, "AutonomousAreaInstance"),
        "ButtonInstance" => ParseAreaInstance(reader, header, "ButtonInstance"),
        "CheckBoxInstance" => ParseAreaInstance(reader, header, "CheckBoxInstance"),
        "RadioButtonInstance" => ParseAreaInstance(reader, header, "RadioButtonInstance"),
        "PageInstance" => ParsePageInstance(reader, header),
        _ => throw new NotSupportedException($"Element class '{className}' isn't implemented by this decoder yet."),
    };

    // --- Shared base sequences ------------------------------------------

    private static MgbField ReadNamedObject(MgbReader reader) => new("NameHash", $"0x{reader.ReadInt():X8}");

    /// <summary>The +0xec slot every Area/Element/Keyframe calls first. The ActionExecuter family
    /// itself isn't decoded yet (different subsystem, see the doc) - if one is actually attached, this
    /// throws rather than silently desyncing.</summary>
    private static MgbField ReadActionCaller(MgbReader reader, MgbHeader header)
    {
        bool hasExecuter = reader.ReadBool();
        if (!hasExecuter)
        {
            return new MgbField("Action", "(none)");
        }
        byte typeIndex = reader.ReadByte();
        string className = DescribeType(header, typeIndex);
        throw new NotSupportedException(
            $"This element has an attached action ('{className}') - the ActionExecuter family isn't decoded yet (see reverse/dunia/mgb_format.md).");
    }

    /// <summary>NamedObject + ActionCaller + 2 flags + category + keyframe list - the base every
    /// leaf widget type (RectShape, TextBase, Image, ListBox, Window, Slider, EditBox) chains through,
    /// inferred from every other subclass in this format explicitly restating its base-slot call (see
    /// the remarks on this class).</summary>
    private static (List<MgbField> Fields, List<MgbNode> Children) ReadElementBase(MgbReader reader, MgbHeader header, string ownerClassName)
    {
        var fields = new List<MgbField> { ReadNamedObject(reader), ReadActionCaller(reader, header) };
        bool hidden = reader.ReadBool();
        bool secondFlag = reader.ReadBool();
        fields.Add(new MgbField("Hidden", (!hidden).ToString())); // doc: flag is inverted into SetVisible
        fields.Add(new MgbField("Flag2", secondFlag.ToString()));
        uint categoryRaw = reader.ReadValue();
        fields.Add(new MgbField("Category", $"0x{categoryRaw & 0x7:X}"));
        uint keyframeCount = reader.ReadValue();
        var keyframes = new List<MgbNode>();
        for (uint i = 0; i < keyframeCount; i++)
        {
            keyframes.Add(ParseKeyframe(reader, header, ownerClassName));
        }
        return (fields, keyframes.Count > 0 ? [new MgbNode("Keyframes", [], keyframes)] : []);
    }

    private static MgbNode ParseArea(MgbReader reader, MgbHeader header, string kind)
    {
        var fields = new List<MgbField> { ReadNamedObject(reader), ReadActionCaller(reader, header) };
        uint ticksDenom = reader.ReadValue();
        uint durationMult = reader.ReadValue();
        uint elementCount = reader.ReadValue();
        fields.Add(new MgbField("TicksDenominator", ticksDenom.ToString()));
        fields.Add(new MgbField("DurationMultiplier", durationMult.ToString()));

        var elements = new List<MgbNode>();
        for (uint i = 0; i < elementCount; i++)
        {
            elements.Add(ParseTypedElement(reader, header));
        }

        ushort left = reader.ReadU16(), top = reader.ReadU16(), right = reader.ReadU16(), bottom = reader.ReadU16();
        fields.Add(new MgbField("StaticBox", $"({left}, {top}, {right}, {bottom})"));

        var children = elements.Count > 0 ? new List<MgbNode> { new("Elements", [], elements) } : [];
        return new MgbNode(kind, fields, children);
    }

    private static MgbNode ParsePage(MgbReader reader, MgbHeader header)
    {
        MgbNode area = ParseArea(reader, header, "Page");
        uint tagCount = reader.ReadInt();
        var tags = new List<MgbField>();
        for (uint i = 0; i < tagCount; i++)
        {
            byte tag = reader.ReadByte();
            uint value = reader.ReadInt();
            tags.Add(new MgbField($"Tag[{tag}]", value.ToString()));
        }
        bool globalSelectionMode = reader.ReadBool();
        var fields = new List<MgbField>(area.Fields) { new("GlobalSelectionMode", globalSelectionMode.ToString()) };
        fields.AddRange(tags);
        return area with { Fields = fields };
    }

    private static MgbNode ParseAreaFixedFloats(MgbReader reader, MgbHeader header, string kind, int floatCount)
    {
        MgbNode area = ParseArea(reader, header, kind);
        var values = new float[floatCount];
        for (int i = 0; i < floatCount; i++)
        {
            values[i] = reader.ReadReal();
        }
        var fields = new List<MgbField>(area.Fields) { new("StateColorsOrGeometry", string.Join(", ", values)) };
        return area with { Fields = fields };
    }

    private static MgbNode ParseCursor(MgbReader reader, MgbHeader header)
    {
        MgbNode area = ParseArea(reader, header, "Cursor");
        short hotspotY = (short)-reader.ReadU16();
        short hotspotX = (short)-reader.ReadU16();
        var fields = new List<MgbField>(area.Fields) { new("Hotspot", $"({hotspotX}, {hotspotY})") };
        return area with { Fields = fields };
    }

    private static MgbNode ParseFocusable(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "Focusable");
        uint neighborTagCount = reader.ReadInt();
        for (uint i = 0; i < neighborTagCount; i++)
        {
            byte a = reader.ReadByte(), b = reader.ReadByte();
            uint c = reader.ReadInt();
            fields.Add(new MgbField($"NeighborTag[{i}]", $"({a}, {b}, {c})"));
        }
        bool inputController = reader.ReadBool();
        fields.Add(new MgbField("InputController", inputController.ToString()));
        return new MgbNode("Focusable", fields, children);
    }

    /// <summary>A bare "Element" - just the shared base sequence, no extra fields of its own. Used as
    /// a generic invisible/grouping node (parallel to how bare "Area" is also directly instantiable).</summary>
    private static MgbNode ParseBareElement(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "Element");
        return new MgbNode("Element", fields, children);
    }

    private static MgbNode ParseRectShape(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "RectShape");
        bool flagA = reader.ReadBool();
        bool flagB = reader.ReadBool();
        float scalar = reader.ReadValueAsFloat();
        fields.Add(new MgbField("FlagA", flagA.ToString()));
        fields.Add(new MgbField("FlagB", flagB.ToString()));
        fields.Add(new MgbField("Scalar", scalar.ToString("0.###")));
        return new MgbNode("RectShape", fields, children);
    }

    /// <summary>Shared base for <see cref="ParseText"/> - also directly instantiable as a bare
    /// "TextBase" per the type table.</summary>
    private static MgbNode ParseTextBase(MgbReader reader, MgbHeader header, string kind)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, kind);
        bool isLocalized = reader.ReadBool();
        if (isLocalized)
        {
            uint stringTableId = reader.ReadInt();
            uint keyHash = reader.ReadInt();
            fields.Add(new MgbField("LocalizedText", $"table=0x{stringTableId:X8} key=0x{keyHash:X8}"));
        }
        else
        {
            uint charCount = reader.ReadInt();
            fields.Add(new MgbField("LiteralText", reader.ReadUtf16((int)charCount)));
        }
        float offsetX = reader.ReadValueAsFloat();
        float offsetY = reader.ReadValueAsFloat();
        fields.Add(new MgbField("Offset", $"({offsetX}, {offsetY})"));
        bool a = reader.ReadBool(), b = reader.ReadBool(), c = reader.ReadBool(), d = reader.ReadBool();
        fields.Add(new MgbField("StyleFlags", $"{a},{b},{c},{d}"));
        if (reader.ReadBool())
        {
            fields.Add(new MgbField("WrapWidth", reader.ReadInt().ToString()));
        }
        return new MgbNode(kind, fields, children);
    }

    private static MgbNode ParseText(MgbReader reader, MgbHeader header)
    {
        MgbNode textBase = ParseTextBase(reader, header, "Text");
        var fields = new List<MgbField>(textBase.Fields);
        fields.Add(ReadResourceRef(reader, "FontFamily")); // LoadFontFamily - inferred, see remarks
        bool a = reader.ReadBool(), b = reader.ReadBool(), c = reader.ReadBool();
        fields.Add(new MgbField("TextFlags", $"{a},{b},{c}"));
        fields.Add(new MgbField("Value1", reader.ReadValueAsFloat().ToString("0.###")));
        fields.Add(new MgbField("Flag", reader.ReadBool().ToString()));
        return textBase with { Fields = fields };
    }

    private static MgbNode ParseImage(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "Image");
        fields.Add(ReadResourceRef(reader, "Material")); // LoadMaterial - inferred, see remarks
        fields.Add(new MgbField("Value1", reader.ReadValueAsFloat().ToString("0.###")));
        fields.Add(new MgbField("Flag", reader.ReadBool().ToString()));
        uint v2 = reader.ReadValue(), v3 = reader.ReadValue();
        fields.Add(new MgbField("PackedValues", $"0x{v2:X8}, 0x{v3:X8}"));
        return new MgbNode("Image", fields, children);
    }

    private static MgbNode ParseListBox(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "ListBox");
        byte unconfirmedSlot = reader.ReadByteB();
        fields.Add(new MgbField("UnconfirmedByte", unconfirmedSlot.ToString()));
        bool a = reader.ReadBool(), b = reader.ReadBool(), c = reader.ReadBool(), d = reader.ReadBool();
        fields.Add(new MgbField("Flags", $"{a},{b},{c},{d}"));
        byte e = reader.ReadByte();
        float scalar = reader.ReadValueAsFloat();
        fields.Add(new MgbField("ByteField", e.ToString()));
        fields.Add(new MgbField("Scalar", scalar.ToString("0.###")));
        if (reader.ReadBool())
        {
            fields.Add(new MgbField("ExtraValue", reader.ReadInt().ToString()));
        }
        var subChildren = new List<MgbNode>();
        if (reader.ReadBool()) subChildren.Add(ParseTypedElement(reader, header));
        if (reader.ReadBool()) subChildren.Add(ParseTypedElement(reader, header));
        if (reader.ReadBool()) subChildren.Add(ParseTypedElement(reader, header));
        children.AddRange(subChildren);
        return new MgbNode("ListBox", fields, children);
    }

    private static MgbNode ParseWindow(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "Window");
        bool a = reader.ReadBool(), b = reader.ReadBool();
        fields.Add(new MgbField("StretchFlags", $"{a},{b}"));
        fields.Add(new MgbField("Sections", "9 window sections (not individually decoded - Window::Read(Stretchable)WindowSection wasn't traced)"));
        return new MgbNode("Window", fields, children);
    }

    private static MgbNode ParseSlider(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "Slider");
        uint v1 = reader.ReadValue(), v2 = reader.ReadValue(), v3 = reader.ReadValue(), v4 = reader.ReadValue(), v5 = reader.ReadValue();
        fields.Add(new MgbField("Range", $"min=0x{v1:X8} max=0x{v2:X8} step=0x{v3:X8}"));
        fields.Add(new MgbField("Extra", $"0x{v4:X8}, 0x{v5:X8}"));
        fields.Add(new MgbField("Flag", reader.ReadBool().ToString()));
        var sub = new List<MgbNode>();
        if (reader.ReadBool()) sub.Add(ParseTypedElement(reader, header));
        if (reader.ReadBool()) sub.Add(ParseTypedElement(reader, header));
        if (reader.ReadBool()) sub.Add(ParseTypedElement(reader, header));
        children.AddRange(sub);
        return new MgbNode("Slider", fields, children);
    }

    private static MgbNode ParseEditBox(MgbReader reader, MgbHeader header)
    {
        (List<MgbField> fields, List<MgbNode> children) = ReadElementBase(reader, header, "EditBox");
        fields.Add(new MgbField("MaxLength", reader.ReadU16().ToString()));
        if (reader.ReadBool())
        {
            fields.Add(new MgbField("PasswordChar", reader.ReadUtf16(1)));
        }
        var sub = new List<MgbNode>();
        if (reader.ReadBool()) sub.Add(ParseTypedElement(reader, header));
        if (reader.ReadBool()) sub.Add(ParseTypedElement(reader, header));
        children.AddRange(sub);
        return new MgbNode("EditBox", fields, children);
    }

    private static MgbNode ParseAreaInstance(MgbReader reader, MgbHeader header, string kind)
    {
        // AutonomousAreaInstance/ButtonInstance/CheckBoxInstance/RadioButtonInstance are confirmed
        // pure forwarders - identical wire shape to AreaInstance itself.
        uint nameLen = reader.ReadInt();
        string name = reader.ReadUtf16((int)nameLen);
        var fields = new List<MgbField> { new("Name", name), ReadResourceRef(reader, "Material") };
        var children = new List<MgbNode>();
        if (reader.ReadBool())
        {
            children.Add(ParseTypedElement(reader, header));
        }
        fields.Add(new MgbField("TrailingValue", $"0x{reader.ReadInt():X8}"));
        return new MgbNode(kind, fields, children);
    }

    private static MgbNode ParsePageInstance(MgbReader reader, MgbHeader header)
    {
        MgbNode areaInstance = ParseAreaInstance(reader, header, "PageInstance");
        uint tagCount = reader.ReadInt();
        var fields = new List<MgbField>(areaInstance.Fields);
        for (uint i = 0; i < tagCount; i++)
        {
            byte a = reader.ReadByte(), b = reader.ReadByte();
            uint c = reader.ReadInt();
            fields.Add(new MgbField($"FocusTag[{i}]", $"({a}, {b}, {c})"));
        }
        return areaInstance with { Fields = fields };
    }

    // --- Keyframes / animation state ------------------------------------

    private static MgbNode ParseKeyframe(MgbReader reader, MgbHeader header, string ownerClassName)
    {
        var fields = new List<MgbField> { ReadNamedObject(reader), ReadActionCaller(reader, header) };
        // Both time and value are read via the same +0x8 (4-byte) slot - time's upper bytes are
        // discarded, per the doc's "2x chained +0x8: first -> u16 time, second -> u32 value".
        ushort time = (ushort)reader.ReadValue();
        uint value = reader.ReadValue();
        fields.Add(new MgbField("Time", time.ToString()));
        fields.Add(new MgbField("Value", value.ToString()));

        string stateKind = ownerClassName + "State";
        MgbNode state = stateKind switch
        {
            "RectShapeState" => ParseRectShapeState(reader),
            "TextState" or "TextBaseState" => ParseTextState(reader),
            "ImageState" => ParseImageState(reader),
            _ => throw new NotSupportedException(
                $"Don't know which *State type a '{ownerClassName}' keyframe uses (only content-specific " +
                "states are inferred from the element's own class name - the generic transform states " +
                "Pos/Rotation/Scale/Rect aren't reachable this way, see reverse/dunia/mgb_format.md)."),
        };
        return new MgbNode("Keyframe", fields, [state]);
    }

    private static List<MgbField> ReadState(MgbReader reader)
    {
        uint v1 = reader.ReadInt();
        uint v2 = reader.ReadInt();
        return [new MgbField("StateValue1", v1.ToString()), new MgbField("StateValue2", v2.ToString())];
    }

    private static List<MgbField> ReadRectState(MgbReader reader)
    {
        var fields = ReadState(reader);
        ushort a = reader.ReadU16(), b = reader.ReadU16(), c = reader.ReadU16(), d = reader.ReadU16();
        fields.Add(new MgbField("Rect", $"({a}, {b}, {c}, {d})"));
        return fields;
    }

    private static MgbNode ParseRectShapeState(MgbReader reader)
    {
        var fields = ReadRectState(reader);
        byte b = reader.ReadByte();
        uint v = reader.ReadInt();
        fields.Add(new MgbField("ByteField", b.ToString()));
        fields.Add(new MgbField("Value", v.ToString()));
        var rgba = new uint[4];
        for (int i = 0; i < 4; i++) rgba[i] = reader.ReadInt();
        fields.Add(new MgbField("Color", $"0x{rgba[0]:X2}{rgba[1]:X2}{rgba[2]:X2}{rgba[3]:X2}"));
        fields.Add(new MgbField("Value2", reader.ReadInt().ToString()));
        byte e = reader.ReadByteB(), f = reader.ReadByteB();
        fields.Add(new MgbField("ExtraBytes", $"{e}, {f}"));
        return new MgbNode("RectShapeState", fields, []);
    }

    /// <summary>Shared by <see cref="ParseTextState"/> - also directly instantiable as a bare
    /// "TextBaseState" per the type table.</summary>
    private static List<MgbField> ReadTextBaseState(MgbReader reader)
    {
        var fields = ReadRectState(reader);
        fields.Add(new MgbField("Scale", reader.ReadReal().ToString("0.###")));
        fields.Add(new MgbField("LineHeight", reader.ReadU16().ToString()));
        return fields;
    }

    private static MgbNode ParseTextState(MgbReader reader)
    {
        var fields = ReadTextBaseState(reader);
        fields.Add(new MgbField("Value", reader.ReadInt().ToString()));
        ushort u16 = reader.ReadU16B();
        byte b1 = reader.ReadByteB(), b2 = reader.ReadByteB();
        fields.Add(new MgbField("AlphaScale", u16.ToString()));
        fields.Add(new MgbField("ColorChannels", $"{b1}, {b2}"));
        ushort off1 = reader.ReadU16();
        short off2 = (short)reader.ReadU16();
        fields.Add(new MgbField("ShadowOffset", $"({off1}, {off2})"));
        return new MgbNode("TextState", fields, []);
    }

    private static MgbNode ParseImageState(MgbReader reader)
    {
        var fields = ReadRectState(reader);
        fields.Add(new MgbField("Value", reader.ReadInt().ToString()));
        byte b1 = reader.ReadByteB(), b2 = reader.ReadByteB();
        fields.Add(new MgbField("ExtraBytes", $"{b1}, {b2}"));
        var uv = new float[4];
        for (int i = 0; i < 4; i++) uv[i] = reader.ReadReal();
        fields.Add(new MgbField("Region", string.Join(", ", uv)));
        bool a = reader.ReadBool(), b = reader.ReadBool(), c = reader.ReadBool();
        fields.Add(new MgbField("Flags", $"{a},{b},{c}"));
        var rgba = new uint[4];
        for (int i = 0; i < 4; i++) rgba[i] = reader.ReadInt();
        fields.Add(new MgbField("Color", $"0x{rgba[0]:X2}{rgba[1]:X2}{rgba[2]:X2}{rgba[3]:X2}"));
        return new MgbNode("ImageState", fields, []);
    }

    // --- Generic property list / resources -------------------------------

    private static MgbNode ParseUserData(MgbReader reader, MgbHeader header)
    {
        var fields = new List<MgbField> { ReadNamedObject(reader) };
        uint count = reader.ReadInt();
        for (uint i = 0; i < count; i++)
        {
            uint key = reader.ReadInt();
            uint typeTag = reader.ReadInt();
            string value = typeTag switch
            {
                2 => reader.ReadReal().ToString("0.###"),
                0xc => reader.ReadBool().ToString(),
                0x10 => ReadLengthPrefixedAnsi(reader),
                0x11 or 0x12 or 0x15 => ReadFullLink(reader, header),
                0x13 => ReadStringResourceExternalId(reader),
                0x14 => "(null)",
                0 or 1 or 3 or 4 or 5 or 6 or 8 or 9 or 10 or 0xb or 0xd or 0xe or 0xf => "(no payload)",
                _ => throw new NotSupportedException($"Unknown UserData property type tag 0x{typeTag:X} for key 0x{key:X8}."),
            };
            fields.Add(new MgbField($"Property[0x{key:X8}] (type 0x{typeTag:X})", value));
        }
        return new MgbNode("UserData", fields, []);
    }

    /// <summary>
    /// <c>magma::BinaryLoadVisitor::VisitFullLink</c> - the actual wire format for
    /// <c>UserData</c> property types <c>0x11</c>/<c>0x12</c>/<c>0x15</c>, decompiled after this
    /// decoder's original "reads nothing" assumption (matching the source RE investigation's own
    /// unresolved gap) turned out to desync real files. Wire record:
    /// <c>[u16 count][byte typeId][count x u32 id]</c>.
    /// </summary>
    private static string ReadFullLink(MgbReader reader, MgbHeader header)
    {
        ushort count = reader.ReadU16B();
        byte typeIndex = reader.ReadByte();
        string typeLabel = typeIndex < header.Types.Count
            ? header.Types[typeIndex].Name ?? $"(unresolved, crc32=0x{header.Types[typeIndex].RawId:X8})"
            : $"(index {typeIndex} out of range)";
        var ids = new uint[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = reader.ReadInt();
        }
        return $"FullLink<{typeLabel}>[{string.Join(", ", ids.Select(id => $"0x{id:X8}"))}]";
    }

    /// <summary><c>magma::BinaryLoadVisitor::VisitStringResourceExternalId</c> - <c>UserData</c>
    /// property type <c>0x13</c>. Wire record: unconditional <c>[u32][u32]</c>, no branching.</summary>
    private static string ReadStringResourceExternalId(MgbReader reader)
    {
        uint a = reader.ReadInt();
        uint b = reader.ReadInt();
        return $"StringResourceExternalId(0x{a:X8}, 0x{b:X8})";
    }

    private static MgbNode ParseMaterial(MgbReader reader)
    {
        var fields = new List<MgbField> { ReadNamedObject(reader) };
        uint texLen = reader.ReadInt();
        if (texLen != 0)
        {
            fields.Add(new MgbField("Texture", MgbReader.DecodeAnsi(reader.ReadBytes((int)texLen))));
        }
        var region = new float[4];
        for (int i = 0; i < 4; i++) region[i] = reader.ReadReal();
        fields.Add(new MgbField("Region", string.Join(", ", region)));
        return new MgbNode("Material", fields, []);
    }

    private static MgbNode ParseFontEntry(MgbReader reader, MgbHeader header, string kind)
    {
        byte typeIndex = reader.ReadByte();
        string typeName = DescribeType(header, typeIndex);
        string s1 = ReadLengthPrefixedAnsi(reader);
        string s2 = ReadLengthPrefixedAnsi(reader);
        return new MgbNode(kind, [new("FontType", typeName), new("String1", s1), new("String2", s2)], []);
    }

    private static MgbNode ParseFontFamily(MgbReader reader) => MgbNode.Leaf("FontFamily", ReadNamedObject(reader));

    private static string ReadLengthPrefixedAnsi(MgbReader reader)
    {
        uint len = reader.ReadInt();
        return len == 0 ? string.Empty : MgbReader.DecodeAnsi(reader.ReadBytes((int)len));
    }

    /// <summary>
    /// <c>LoadMaterial</c>/<c>LoadFontFamily</c> - never traced to byte level in the source RE
    /// investigation, inferred to follow this format's universal length-prefixed-ANSI-string pattern.
    /// See this class's remarks.
    /// </summary>
    private static MgbField ReadResourceRef(MgbReader reader, string label)
        => new(label, ReadLengthPrefixedAnsi(reader) is { Length: > 0 } s ? s : "(none)");
}
