namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// The widget an <see cref="MgbElement"/> wraps. Read from the tail of <c>VisitElement</c> via
/// <c>widget-&gt;Accept(visitor)</c> - a call that is easy to miss, because <c>VisitElement</c>'s own
/// decompile shows no serialiser use after the keyframe loop.
/// </summary>
public abstract class MgbWidget : MgbRecord
{
    /// <summary>One of the 14 classes <c>Factory::MakeElement</c> accepts.</summary>
    public abstract string TypeName { get; }

    public static MgbWidget Create(string typeName) => typeName switch
    {
        "Placeholder" => new MgbPlaceholder(),
        "RectShape" => new MgbRectShape(),
        "Image" => new MgbImage(),
        "Text" => new MgbTextWidget(),
        "AreaInstance" => new MgbAreaInstance("AreaInstance"),
        "AutonomousAreaInstance" => new MgbAreaInstance("AutonomousAreaInstance"),
        "ButtonInstance" => new MgbAreaInstance("ButtonInstance"),
        "CheckBoxInstance" => new MgbAreaInstance("CheckBoxInstance"),
        "RadioButtonInstance" => new MgbAreaInstance("RadioButtonInstance"),
        "PageInstance" => new MgbPageInstance(),
        "ListBox" => new MgbListBox(),
        "EditBox" => new MgbEditBox(),
        "Slider" => new MgbSlider(),
        "Window" => new MgbWindow(),
        _ => throw new MgbFormatException(
            $"'{typeName}' is not one of the 14 widget classes Factory::MakeElement can construct"),
    };
}

/// <summary>A layout slot. <c>Placeholder</c> has no <c>Visit</c> override - the inherited
/// <c>Visitor::VisitPlaceholder</c> (<c>0x09606ae0</c>) is an empty no-op - so its widget body is
/// genuinely zero bytes. (It still gets the full <see cref="MgbElement"/> header like every other
/// widget; conflating those two facts cost an earlier decoder a lot of grief.)</summary>
public sealed class MgbPlaceholder : MgbWidget
{
    public override string TypeName => "Placeholder";
    public override void Serialize(IMgbCodec c, MgbContext ctx) { }
}

/// <summary><c>VisitRectShape</c> (<c>0x0a05db40</c>).</summary>
public sealed class MgbRectShape : MgbWidget
{
    public bool IsOutlined;
    public bool IsFilled;

    /// <summary>Read as a <c>u32</c>; the engine keeps only the low byte.</summary>
    public uint BlendingMode;

    public override string TypeName => "RectShape";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.Bool("ISOUTLINED", ref IsOutlined);
        c.Bool("ISFILLED", ref IsFilled);
        c.U32("BLENDINGMODE", ref BlendingMode);
    }
}

/// <summary><c>VisitImage</c> (<c>0x0a060e80</c>).</summary>
public sealed class MgbImage : MgbWidget
{
    public MgbResourceRef Material = new();
    public uint BlendingMode;
    public bool AlphaBlendFirst;

    /// <summary>The engine packs these two into one byte's low and high nibbles.</summary>
    public uint AddressingModeU;
    public uint AddressingModeV;

    public override string TypeName => "Image";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        Material.Serialize(c, ctx);
        c.U32("BLENDINGMODE", ref BlendingMode);
        c.Bool("ALPHABLENDFIRST", ref AlphaBlendFirst);
        c.U32("ADDRESSINGMODEU", ref AddressingModeU);
        c.U32("ADDRESSINGMODEV", ref AddressingModeV);
    }
}

/// <summary>
/// <c>VisitTextBase</c> (<c>0x0a0616d0</c>) - the text content and layout shared by every text
/// widget. Not directly constructible; <see cref="MgbTextWidget"/> extends it.
/// </summary>
public abstract class MgbTextBase : MgbWidget
{
    /// <summary>Selects between a string-table reference and inline text.</summary>
    public bool UseStringTable;

    public uint TableId;
    public uint ResourceId;

    /// <summary>Inline UTF-16 text, when <see cref="UseStringTable"/> is false.</summary>
    public byte[] String = [];

    public uint AlignmentX;
    public uint AlignmentY;
    public bool Wrapping;
    public bool Clipping;
    public bool Ellipsis;
    public bool AutoSized;

    /// <summary>Null when the gate byte is clear.</summary>
    public uint? SliderLink;

    /// <summary>The inline text as a string, for display and editing. Only meaningful when
    /// <see cref="UseStringTable"/> is false.</summary>
    public string Text
    {
        get => MgbText.Utf16(String);
        set => String = MgbText.ToUtf16(value);
    }

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.Bool("useStringTable", ref UseStringTable);
        if (UseStringTable)
        {
            c.U32("TABLEID", ref TableId);
            c.U32("RESOURCEID", ref ResourceId);
        }
        else
        {
            c.Utf16String("STRING", ref String);
        }
        c.U32("ALIGNMENTX", ref AlignmentX);
        c.U32("ALIGNMENTY", ref AlignmentY);
        c.Bool("WRAPPING", ref Wrapping);
        c.Bool("CLIPPING", ref Clipping);
        c.Bool("ELLIPSIS", ref Ellipsis);
        c.Bool("AUTOSIZED", ref AutoSized);

        bool hasSliderLink = SliderLink.HasValue;
        c.Bool("hasSliderLink", ref hasSliderLink);
        if (hasSliderLink)
        {
            uint link = SliderLink ?? 0;
            c.U32("SLIDERLINK", ref link);
            SliderLink = link;
        }
        else if (c.IsReading)
        {
            SliderLink = null;
        }
    }
}

/// <summary><c>VisitText</c> (<c>0x0a0610e0</c>): <see cref="MgbTextBase"/> plus a font family and
/// style flags.</summary>
public sealed class MgbTextWidget : MgbTextBase
{
    public MgbResourceRef FontFamily = new();
    public bool Bold;
    public bool Italics;
    public bool Underlined;
    public uint BlendingMode;
    public bool AlphaBlendFirst;

    public override string TypeName => "Text";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        FontFamily.Serialize(c, ctx);
        c.Bool("BOLD", ref Bold);
        c.Bool("ITALICS", ref Italics);
        c.Bool("UNDERLINED", ref Underlined);
        c.U32("BLENDINGMODE", ref BlendingMode);
        c.Bool("ALPHABLENDFIRST", ref AlphaBlendFirst);
    }
}

/// <summary>
/// <c>VisitAreaInstance</c> (<c>0x0a060a80</c>): an embedded instance of another area.
/// <c>AutonomousAreaInstance</c>, <c>ButtonInstance</c>, <c>CheckBoxInstance</c> and
/// <c>RadioButtonInstance</c> are pure forwards that add nothing, so they share this body and
/// differ only in which <see cref="MgbElement"/> wrapper <c>Factory::MakeElement</c> gives them.
/// </summary>
public class MgbAreaInstance(string typeName) : MgbWidget
{
    /// <summary>UTF-16; the name of the area being instanced.</summary>
    public byte[] Label = [];

    public MgbResourceRef Material = new();
    public MgbAreaLink? Link;
    public uint IndexOffset;

    private readonly string _typeName = typeName;
    public override string TypeName => _typeName;

    public string LabelText
    {
        get => MgbText.Utf16(Label);
        set => Label = MgbText.ToUtf16(value);
    }

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.Utf16String("LABEL", ref Label);
        Material.Serialize(c, ctx);
        SerializeOptional(c, ctx, "hasLink", ref Link);
        c.U32("INDEXOFFSET", ref IndexOffset);
    }
}

/// <summary>One <c>DEFAULTFOCUS</c> entry of a <see cref="MgbPageInstance"/>.</summary>
public sealed class MgbFocusTag : MgbRecord
{
    public byte FromDirection;
    public byte FromDirection2;
    public uint Id;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U8("DEFAULT_FROM_DIRECTION", ref FromDirection);
        c.U8("DEFAULT_FROM_DIRECTION_2", ref FromDirection2);
        c.U32("DEFAULTFOCUS", ref Id);
    }
}

/// <summary><c>VisitPageInstance</c> (<c>0x0a05f3f0</c>): an <see cref="MgbAreaInstance"/> plus its
/// default-focus table.</summary>
public sealed class MgbPageInstance() : MgbAreaInstance("PageInstance")
{
    public List<MgbFocusTag> FocusTags = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        SerializeList(c, ctx, FocusTags);
    }
}

/// <summary>
/// <c>VisitListBox</c> (<c>0x0a05f680</c>).
/// </summary>
/// <remarks>
/// Field widths and order are offset-verified; the names are inferred from the class's XML
/// vocabulary (<c>AUTOCENTER</c>, <c>BUTTONCOUNT</c>, <c>HEADERFOOTERPOS</c>, <c>ITEMSPACING</c>,
/// <c>SLIDESELITEM</c>, <c>VERTICALSPACING</c>, <c>WRAPAROUND</c>, and the three link elements)
/// rather than from the per-field offset join, which was not run for this class. See
/// research/mgb-field-names.md.
/// </remarks>
public sealed class MgbListBox : MgbWidget
{
    public byte HeaderFooterPos;
    public bool AutoCenter;
    public bool WrapAround;
    public bool SlideSelItem;
    public bool Flag4;
    public byte ButtonCount;
    public uint ItemSpacing;

    /// <summary>Null when the gate byte is clear.</summary>
    public uint? SliderLink;

    /// <summary>The three embedded links, in wire order: header, item, footer.</summary>
    public MgbAreaLink?[] Links = new MgbAreaLink?[3];

    public override string TypeName => "ListBox";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U8("HEADERFOOTERPOS", ref HeaderFooterPos);
        c.Bool("AUTOCENTER", ref AutoCenter);
        c.Bool("WRAPAROUND", ref WrapAround);
        c.Bool("SLIDESELITEM", ref SlideSelItem);
        c.Bool("flag4", ref Flag4);
        c.U8("BUTTONCOUNT", ref ButtonCount);
        c.U32("ITEMSPACING", ref ItemSpacing);

        bool hasSliderLink = SliderLink.HasValue;
        c.Bool("hasSliderLink", ref hasSliderLink);
        if (hasSliderLink)
        {
            uint link = SliderLink ?? 0;
            c.U32("SLIDERLINK", ref link);
            SliderLink = link;
        }
        else if (c.IsReading)
        {
            SliderLink = null;
        }

        string[] names = ["HEADERLINK", "ITEMLINK", "FOOTERLINK"];
        for (int i = 0; i < 3; i++)
        {
            MgbAreaLink? link = Links[i];
            SerializeOptional(c, ctx, names[i], ref link);
            Links[i] = link;
        }
    }
}

/// <summary><c>VisitEditBox</c> (<c>0x0a05ec80</c>). Link names inferred from the class's
/// vocabulary (<c>FIELDLINK</c>, <c>CURSORLINK</c>).</summary>
public sealed class MgbEditBox : MgbWidget
{
    /// <summary>Read as a <c>u32</c>; the engine keeps the low 16 bits.</summary>
    public uint MaxLength;

    /// <summary>A single UTF-16 code unit, or null when the gate byte is clear.</summary>
    public byte[]? PasswordChar;

    public MgbAreaLink?[] Links = new MgbAreaLink?[2];

    public override string TypeName => "EditBox";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U32("maxLength", ref MaxLength);

        bool hasPasswordChar = PasswordChar is not null;
        c.Bool("hasPasswordChar", ref hasPasswordChar);
        if (hasPasswordChar)
        {
            byte[] ch = PasswordChar ?? new byte[2];
            c.Blob("passwordChar", ref ch, 2);
            PasswordChar = ch;
        }
        else if (c.IsReading)
        {
            PasswordChar = null;
        }

        string[] names = ["FIELDLINK", "CURSORLINK"];
        for (int i = 0; i < 2; i++)
        {
            MgbAreaLink? link = Links[i];
            SerializeOptional(c, ctx, names[i], ref link);
            Links[i] = link;
        }
    }
}

/// <summary><c>VisitSlider</c> (<c>0x0a05eb10</c>). <c>Slider::SetRange</c> takes the first two
/// values; the remaining names are inferred from the class's vocabulary.</summary>
public sealed class MgbSlider : MgbWidget
{
    public uint RangeMin;
    public uint RangeMax;
    public uint Field2;
    public uint Field3;
    public uint Field4;
    public bool Orientation;

    /// <summary>Four embedded links: track, knob/handle, header, footer.</summary>
    public MgbAreaLink?[] Links = new MgbAreaLink?[4];

    public override string TypeName => "Slider";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U32("RANGEMIN", ref RangeMin);
        c.U32("RANGEMAX", ref RangeMax);
        c.U32("field2", ref Field2);
        c.U32("field3", ref Field3);
        c.U32("field4", ref Field4);
        c.Bool("ORIENTATION", ref Orientation);

        string[] names = ["TRACKLINK", "KNOBLINK", "HEADERLINK", "FOOTERLINK"];
        for (int i = 0; i < 4; i++)
        {
            MgbAreaLink? link = Links[i];
            SerializeOptional(c, ctx, names[i], ref link);
            Links[i] = link;
        }
    }
}

/// <summary>One of a <see cref="MgbWindow"/>'s nine 9-patch sections.
/// <c>ReadWindowSection</c> (<c>0x0a060c40</c>) plus, for the stretchable ones,
/// <c>ReadStretchableWindowSection</c> (<c>0x0a060d20</c>)'s extra field.</summary>
public sealed class MgbWindowSection : MgbRecord
{
    public MgbResourceRef Material = new();
    public uint BlendingMode;
    public bool AlphaBlendFirst;
    public bool FlipHorizontal;
    public bool FlipVertical;
    public bool Rotated;

    /// <summary>Only on the wire for the stretchable sections (indices 0 and 5-8).</summary>
    public uint StretchMode;

    public bool Stretchable;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        Material.Serialize(c, ctx);
        c.U32("BLENDINGMODE", ref BlendingMode);
        c.Bool("ALPHABLENDFIRST", ref AlphaBlendFirst);
        c.Bool("FLIPHORIZONTAL", ref FlipHorizontal);
        c.Bool("FLIPVERTICAL", ref FlipVertical);
        c.Bool("ROTATED", ref Rotated);
        if (Stretchable)
        {
            c.U32("STRETCHMODE", ref StretchMode);
        }
    }
}

/// <summary><c>VisitWindow</c> (<c>0x0a060d70</c>): a 9-patch border layout.</summary>
public sealed class MgbWindow : MgbWidget
{
    /// <summary>Section names in the engine's own 0-8 order, from <c>LoadVisitor::ReadWindow</c>.</summary>
    public static readonly string[] SectionNames =
    [
        "FILL", "TOP_LEFT_CORNER", "TOP_RIGHT_CORNER", "BOTTOM_LEFT_CORNER",
        "BOTTOM_RIGHT_CORNER", "TOP_EDGE", "LEFT_EDGE", "RIGHT_EDGE", "BOTTOM_EDGE",
    ];

    /// <summary>Index 0 and 5-8 are stretchable; the four corners are not.</summary>
    public static bool IsStretchable(int index) => index == 0 || index >= 5;

    public bool SingleCornerMaterial;
    public bool SingleEdgeMaterial;
    public MgbWindowSection[] Sections = [.. Enumerable.Range(0, 9)
        .Select(i => new MgbWindowSection { Stretchable = IsStretchable(i) })];

    public override string TypeName => "Window";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.Bool("SINGLECORNERMATERIAL", ref SingleCornerMaterial);
        c.Bool("SINGLEEDGEMATERIAL", ref SingleEdgeMaterial);
        for (int i = 0; i < 9; i++)
        {
            Sections[i].Stretchable = IsStretchable(i);
            Sections[i].Serialize(c, ctx);
        }
    }
}
