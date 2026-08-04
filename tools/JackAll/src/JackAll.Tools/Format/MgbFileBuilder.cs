namespace JackAll.Tools.Format;

/// <summary>A widget's on-screen rect in the same 4x u16 shape <see cref="MgbBody"/> reads as
/// <c>StaticBox</c> - left/top/right/bottom, in the page's own coordinate space (see <c>PAGESIZE</c>).</summary>
public readonly record struct MgbBox(ushort Left, ushort Top, ushort Right, ushort Bottom);

/// <summary>
/// Builds valid, from-scratch <c>.mgb</c> byte content - the write-side counterpart to
/// <see cref="MgbHeader"/>/<see cref="MgbBody"/>, using <see cref="MgbWriter"/> to emit exactly the
/// field sequences those two decode (see docs/docs/file-formats/mgb.md). Deliberately narrow: only the
/// element shapes needed to build a new <c>Page</c> full of <c>CheckBox</c>/<c>Button</c> rows are
/// implemented (the well-validated, well-understood part of the format - see mgb.md's "Next direction"
/// note) - anything else (materials, fonts, keyframe animation, other widget types) is written as
/// empty/absent rather than guessed at.
/// </summary>
/// <remarks>
/// Every value written here round-trips through the *existing*, independently-written read path
/// (<see cref="MgbHeader.Decode"/>/<see cref="MgbBody.ParsePackage"/>) in this project's test suite -
/// that's the actual correctness check for this class, not a hand-derived byte table.
/// </remarks>
public static class MgbFileBuilder
{
    /// <summary>One row to appear on a built Mods page - a checkbox with a starting on/off value.
    /// <paramref name="NameHash"/> defaults to <c>CRC32(Label)</c> if the caller doesn't need a
    /// specific one (real files often just hash the element's own class name when otherwise unnamed -
    /// see mgb.md's <c>Cursor</c> example - but a per-row label hash is more useful for later matching
    /// a toggled instance back to its own row).</summary>
    public readonly record struct ModCheckBoxRow(string Label, bool InitialValue)
    {
        public uint NameHash => MgbTypeTable.ComputeHash(Label);
    }

    /// <summary>
    /// Builds a complete, standalone <c>.mgb</c> file: a single top-level <c>Page</c> containing one
    /// <c>CheckBox</c> element per <paramref name="rows"/>, stacked vertically. No materials/fonts/
    /// actions are attached - this is the minimal content needed to exist as a real, parseable Magma
    /// page, not a styled, ready-to-ship screen (see the class remarks).
    /// </summary>
    /// <param name="pageNameLabel">Human-readable seed for the page's own <c>NameHash</c> (hashed the
    /// same way <see cref="ModCheckBoxRow"/> hashes its label).</param>
    /// <param name="pageSize">The page's <c>PAGESIZE</c> - pass an existing screen's own size (see
    /// mgb.md - real files use e.g. 1024x768 or 1280x800) so this page lines up with the rest of the UI.</param>
    /// <param name="rows">Checkbox rows, top to bottom.</param>
    /// <param name="rowHeight">Vertical spacing between rows, in the page's own coordinate space.</param>
    public static byte[] BuildModsPage(string pageNameLabel, (ushort Width, ushort Height) pageSize,
        IReadOnlyList<ModCheckBoxRow> rows, ushort rowHeight = 32)
    {
        // "Button" isn't used by this method itself, but is included so the result is immediately
        // splice-ready for MgbPageEditor.AddButtonToTopLevelPage, which refuses to grow the type table
        // after the fact (growing it would shift every body offset - see its own remarks).
        (byte[] header, IReadOnlyDictionary<string, byte> typeIds) = BuildHeader(["Page", "CheckBox", "Button"]);

        var w = new MgbWriter();
        w.WriteBytes(header);

        WritePackagePreamble(w, pageNameLabel, pageSize);

        w.WriteInt(1); // areaCount = 1 (our single Page)

        var children = new List<Action<MgbWriter>>();
        for (int i = 0; i < rows.Count; i++)
        {
            ModCheckBoxRow row = rows[i];
            ushort top = (ushort)(i * rowHeight);
            var box = new MgbBox(0, top, pageSize.Width, (ushort)(top + rowHeight));
            children.Add(w2 => WriteCheckBox(w2, typeIds["CheckBox"], row.NameHash, box, row.InitialValue));
        }

        w.WriteByte(typeIds["Page"]);
        WriteArea(w, MgbTypeTable.ComputeHash(pageNameLabel), children, new MgbBox(0, 0, pageSize.Width, pageSize.Height));
        w.WriteInt(0);       // Page's own tagCount
        w.WriteBool(false);  // Page's own GlobalSelectionMode

        WritePackageTrailer(w);

        return w.ToArray();
    }

    // --- Header / type table --------------------------------------------

    /// <summary>Writes the fixed magic/sentinel/version/flag prefix plus a type table containing
    /// exactly <paramref name="classNames"/> (in order), and returns each name's resolved 1-based
    /// type-id byte (the value a body read actually uses - see mgb.md's confirmed off-by-one: table
    /// index <c>i</c> -&gt; type-id byte <c>i+1</c>). A real file's type table is normally a fixed,
    /// ~166-entry, build-wide constant (see mgb.md) - this deliberately writes only what's referenced,
    /// which is legal (unresolved/empty slots are already a normal, documented case) and keeps a hand
    /// -built file's header small and easy to eyeball.</summary>
    public static (byte[] Header, IReadOnlyDictionary<string, byte> TypeIds) BuildHeader(IReadOnlyList<string> classNames)
    {
        if (classNames.Count > 254)
        {
            throw new ArgumentException("Type table count byte can't express more than 254 entries (255 minus 1 for the count-vs-entries offset - see MgbHeader.Decode).");
        }

        var w = new MgbWriter();
        w.WriteAscii("MAGMA");
        w.WriteByte(0xCD); w.WriteByte(0x00); w.WriteByte(0x00); w.WriteByte(0xAB); // sentinel (only byte 8, 0xAB, is checked)
        w.WriteInt(MgbHeader.ExpectedVersion);
        w.WriteByte(0x00); // flag byte - purpose unidentified, 0 matches every real sample seen so far
        w.WriteByte((byte)(classNames.Count + 1)); // type-table count byte (entries = count - 1)

        var typeIds = new Dictionary<string, byte>(classNames.Count);
        for (int i = 0; i < classNames.Count; i++)
        {
            w.WriteInt(MgbTypeTable.ComputeHash(classNames[i]));
            typeIds[classNames[i]] = (byte)(i + 1);
        }

        return (w.ToArray(), typeIds);
    }

    /// <summary>Everything <c>VisitPackage</c> reads between the header and <c>areaCount</c>'s own
    /// value (see mgb.md's "VisitPackage - the preamble"): no config-block content (65 zeroed 32-bit
    /// values - real files forward these to Package property setters this builder doesn't need), no
    /// package-level UserData properties, <paramref name="pageSize"/> as <c>PAGESIZE</c>, a zero
    /// <c>DISPLAYOFFSET</c>, and zero materials/fonts. Stops right before <c>areaCount</c>'s own count
    /// field - callers write that themselves, then that many area records, then call
    /// <see cref="WritePackageTrailer"/>.</summary>
    private static void WritePackagePreamble(MgbWriter w, string pageNameLabel, (ushort Width, ushort Height) pageSize)
    {
        for (int i = 0; i < 65; i++) w.WriteValue(0); // config block

        w.WriteInt(MgbTypeTable.ComputeHash(pageNameLabel)); // Package's own UserData NamedObject
        w.WriteInt(0); // Package's own UserData property count

        w.WriteU16(pageSize.Width); w.WriteU16(pageSize.Height); // PAGESIZE
        w.WriteU16(0); w.WriteU16(0);                            // DISPLAYOFFSET

        w.WriteInt(0); w.WriteInt(0); // materialCount, materialUnknownField
        w.WriteInt(0);                // fontSubstCount
        w.WriteInt(0);                // fontDeclCount
        w.WriteInt(0);                // fontFamilyCount
    }

    private static void WritePackageTrailer(MgbWriter w)
    {
        w.WriteBool(false); // hasGlobalFocusArea
        w.WriteBool(false); // hasSecondArea
        w.WriteInt(0);      // defaultMaterialNameLen
    }

    // --- Element shapes ---------------------------------------------------

    /// <summary>The shared <c>Area</c> wire shape every top-level/child area-like element (Page,
    /// CheckBox, Button, Cursor, bare Area) starts with - see mgb.md's <c>VisitArea</c> entry. Writes
    /// an empty <c>UserData</c> (name hash + 0 properties), no attached action, zeroed
    /// ticks-denominator/duration-multiplier, then <paramref name="children"/> (each invoked in order,
    /// responsible for writing its own leading type-id byte), then the trailing <c>StaticBox</c>.</summary>
    internal static void WriteArea(MgbWriter w, uint nameHash, IReadOnlyList<Action<MgbWriter>> children, MgbBox box)
    {
        w.WriteInt(nameHash);   // NamedObject
        w.WriteInt(0);          // UserData property count = 0
        w.WriteBool(false);     // ActionCaller: no attached action
        w.WriteValue(0);        // ticksDenominator
        w.WriteValue(0);        // durationMultiplier
        w.WriteValue((uint)children.Count);
        foreach (Action<MgbWriter> child in children)
        {
            child(w);
        }
        w.WriteU16(box.Left); w.WriteU16(box.Top); w.WriteU16(box.Right); w.WriteU16(box.Bottom);
    }

    /// <summary>A <c>CheckBox</c> element: the <c>Area</c> base (no children of its own) plus the 12
    /// fixed floats <see cref="MgbBody"/> reads as <c>StateColorsOrGeometry</c> (see mgb.md's
    /// <c>VisitCheckBox</c> entry - plausibly 3 states x RGBA). Written as all-zero for now: enough to
    /// parse back correctly and exist as a real, selectable row, not yet styled.</summary>
    internal static void WriteCheckBox(MgbWriter w, byte typeId, uint nameHash, MgbBox box, bool initialValue)
    {
        w.WriteByte(typeId);
        WriteArea(w, nameHash, [], box);
        for (int i = 0; i < 12; i++)
        {
            // The one bit of real, meaningful content this builder writes into the fixed-float block:
            // put the row's starting on/off value in the first float as a cheap, human-visible marker
            // until CheckBox's real field meanings (see mgb.md) are decoded. Not read by the engine as
            // a checked-state flag - that's presumably driven by a UserData property or native code
            // this builder doesn't attach yet (see the class remarks).
            w.WriteReal(i == 0 && initialValue ? 1f : 0f);
        }
    }

    /// <summary>A <c>Button</c> element: the <c>Area</c> base plus the 6 fixed floats
    /// <see cref="MgbBody"/> reads as <c>StateColorsOrGeometry</c> (see mgb.md's <c>VisitButton</c>
    /// entry). No action attached - see <see cref="MgbPageEditor"/> for adding one to an existing
    /// button once real click-to-native-code dispatch is worked out.</summary>
    internal static void WriteButton(MgbWriter w, byte typeId, uint nameHash, MgbBox box)
    {
        w.WriteByte(typeId);
        WriteArea(w, nameHash, [], box);
        for (int i = 0; i < 6; i++)
        {
            w.WriteReal(0f);
        }
    }
}
