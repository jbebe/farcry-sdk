namespace JackAll.Tools.Mgb;

/// <summary>One named value set from <c>magma::Util</c>'s tag table.</summary>
/// <remarks>Entries are held as explicit value/name pairs rather than a bare array indexed by value:
/// most groups happen to run 0..n-1, but several (the controller and event groups) do not, and an
/// index-based list would silently mislabel those if one were ever added here.</remarks>
public sealed class MgbEnum
{
    private readonly (uint Value, string Name)[] _entries;

    public MgbEnum(int group, params (uint Value, string Name)[] entries)
    {
        Group = group;
        _entries = entries;
        Names = [.. entries.Select(e => e.Name)];
    }

    /// <summary>Builds a group whose values run 0..n-1, which most of them do.</summary>
    public MgbEnum(int group, params string[] names)
        : this(group, [.. names.Select((n, i) => ((uint)i, n))])
    {
    }

    /// <summary><c>Util::GetType</c>'s tag-group number.</summary>
    public int Group { get; }

    /// <summary>The names, in table order - what a picker offers.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>The name for a stored value, or null when the value isn't in the table (which a
    /// file is free to contain - the engine's own lookup returns a "not found" marker for it).</summary>
    public string? NameFor(uint value)
    {
        foreach ((uint entryValue, string name) in _entries)
        {
            if (entryValue == value)
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>The value a name stands for. Case-insensitive, matching <c>Util::GetType</c>'s own
    /// <c>strcasecmp</c>.</summary>
    public bool TryValueFor(string name, out uint value)
    {
        foreach ((uint entryValue, string entryName) in _entries)
        {
            if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
            {
                value = entryValue;
                return true;
            }
        }
        value = 0;
        return false;
    }
}

/// <summary>
/// The named value sets Magma's XML loader resolves through <c>magma::Util::GetType</c>.
/// </summary>
/// <remarks>
/// <c>LoadVisitor</c> authors these fields as names, not numbers, and <c>GetType</c>/<c>GetTag</c>
/// (<c>0x0a03ba50</c> / <c>0x0a03b831</c>) are a pair of linear scans over one static table:
/// <code>
/// entries = *(Entry**)(ms_tagTable + group * 8 + 4);   // Entry { u32 value; const char* name; }
/// count   = *(int*)   (ms_tagTable + group * 8);
/// </code>
/// No code path holds any of these names as a literal, so they were read out of <c>ms_tagTable</c> at
/// <c>0x0a34ba80</c> in the debug <c>FarCry2_server</c> ELF. Which group belongs to which field was
/// then pinned down from the other side: every <c>call Util::GetType</c> in the binary is preceded by
/// a <c>push imm8</c> of its group, so scanning for those call sites gives the group each
/// <c>ReadX</c> actually passes. The full 22-group dump is in
/// docs/docs/file-formats/mgb-field-names.md.
///
/// Only groups that name a *value* a record stores are here. Deliberately absent:
/// <list type="bullet">
/// <item>groups 4/5/14/15 name the <i>slots</i> of <c>Button</c>/<c>CheckBox</c>'s
/// <c>TIMINGS</c> array (ENABLED, PRESSED, …) - they label which row is which, they are not a value
/// any field holds.</item>
/// <item>group 16 (<c>Area Link</c>, <c>Integer</c>, <c>Float</c>, …) looks like <c>UserData</c>'s
/// property type but uses a different numbering from the wire tags <c>MgbProperty</c> stores
/// (<c>0x02</c> u32, <c>0x07</c> float, <c>0x10</c> string …). Wiring it would mislabel every
/// property.</item>
/// <item>groups 8, 12, 13 and 17-26 have no call site in any <c>ReadX</c> that writes a field this
/// model carries - they belong to the editor, the texture pipeline and the handler/event
/// subsystem.</item>
/// </list>
/// </remarks>
public static class MgbEnums
{
    /// <summary>Group 0 - <c>INTERPOLATION</c> on a keyframe: the curve the frame eases along.</summary>
    /// <remarks>Confirmed a plain value, not a type id: <c>ReadKeyframe</c> (<c>0x0a06c5a0</c>) calls
    /// <c>Util::GetType(0, …)</c> at <c>+0xec</c>. Earlier notes described this field as a
    /// "timing-strategy type id", which it is not - that is <c>AreaLink</c>'s <c>TIMING</c> slot.</remarks>
    public static readonly MgbEnum Interpolation = new(
        0, "None", "Linear", "Square", "Root", "Sin", "Circle", "CircleDecel");

    /// <summary>Group 1 - <c>ALIGNMENTX</c> (alias <c>ALIGNMENT</c>) on a text widget.</summary>
    public static readonly MgbEnum AlignmentX = new(1, "LEFT", "CENTER", "RIGHT", "JUSTIFY");

    /// <summary>Group 2 - <c>ALIGNMENTY</c> on a text widget.</summary>
    public static readonly MgbEnum AlignmentY = new(2, "TOP", "CENTER", "BOTTOM");

    /// <summary>
    /// Group 6 - <c>HEADERFOOTERPOS</c> on a list box. Recorded for completeness but *not* offered
    /// by the editor.
    /// </summary>
    /// <remarks>
    /// The byte this model calls <c>MgbListBox.HeaderFooterPos</c> holds values like 10, 14, 206,
    /// 236 and 248 across the shipped packages, which a two-entry table cannot be. <c>ListBox</c>'s
    /// field names were inferred from the class's XML vocabulary rather than recovered from the
    /// per-field offset join, so that byte is likely not this field. Offering a picker over it would
    /// invent a meaning the evidence does not support.
    /// </remarks>
    public static readonly MgbEnum HeaderFooterPos = new(6, "Top and Bottom", "Left and Right");

    /// <summary>Group 7 - <c>ORIENTATION</c> on a slider. Stored as a single bool on the wire, so
    /// false is <c>Horizontal</c> and true is <c>Vertical</c>.</summary>
    public static readonly MgbEnum Orientation = new(7, "Horizontal", "Vertical");

    /// <summary>
    /// Group 9 - <c>BLENDINGMODE</c> on <c>Image</c>, <c>RectShape</c>, <c>Text</c> and
    /// <c>WindowSection</c>. The engine keeps only the low byte.
    /// </summary>
    /// <remarks>Six of the 27 appear in the shipped packages: 0 <c>Normal</c> (29,870 uses),
    /// 15 <c>Lighten 2X</c> (40), 16 <c>Lighten 4X</c> (10), 17 <c>Add</c> (1,840),
    /// 20 <c>Multiply</c> (330) and 21 <c>Modulate</c> (1,000).</remarks>
    public static readonly MgbEnum BlendingMode = new(
        9,
        "Normal", "Negative", "Plain Color", "Plain Alpha", "Silhouette",
        "Burn", "Burn 2X", "Burn 4X",
        "Dodge", "Dodge 2X", "Dodge 4X",
        "Darken", "Darken 2X", "Darken 4X",
        "Lighten", "Lighten 2X", "Lighten 4X",
        "Add", "Ghost", "Invert", "Multiply", "Modulate", "Only Alpha",
        "Custom1", "Custom2", "Custom3", "Custom4");

    /// <summary>Group 10 - <c>ADDRESSINGMODEU</c>/<c>ADDRESSINGMODEV</c> on an image. The engine
    /// packs the pair into one byte's two nibbles.</summary>
    public static readonly MgbEnum AddressingMode = new(10, "Wrap", "Mirror", "Clamp", "Border");

    /// <summary>Group 11 - <c>MASKMODE</c> on an element. The engine keeps only the low 3 bits.</summary>
    public static readonly MgbEnum MaskMode = new(11, "NOMASK", "SETMASK", "USEMASK", "USEMASK_INVERTED");
}
