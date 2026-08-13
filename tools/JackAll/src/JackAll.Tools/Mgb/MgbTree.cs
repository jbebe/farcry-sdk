namespace JackAll.Tools.Mgb;

/// <summary>One <c>NEIGHBOR</c> entry of a focusable element.</summary>
public sealed class MgbNeighbor : MgbRecord
{
    public byte Controller;
    public byte Direction;
    public uint Id;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U8("CONTROLLER", ref Controller);
        c.U8("DIRECTION", ref Direction);
        c.NameId("ID", ref Id);
    }
}

/// <summary>
/// <c>VisitFocusable</c> (<c>0x0a05fc80</c>)'s own fields, appended after the shared element body
/// *and* the widget body - because <c>VisitFocusable</c> calls <c>VisitElement</c> first, and
/// <c>VisitElement</c> ends by dispatching into the widget.
/// </summary>
/// <remarks><c>VisitPageFocusable</c>, <c>VisitCheckable</c> and <c>VisitRadioable</c> are pure
/// forwards to <c>VisitFocusable</c>, so all four wrappers read exactly this.</remarks>
public sealed class MgbFocusableTail : MgbRecord
{
    public List<MgbNeighbor> Neighbors = [];
    public byte InputFilter;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        SerializeList(c, ctx, "NEIGHBORS", "NEIGHBOR", Neighbors);
        c.U8("INPUTFILTER", ref InputFilter);
    }
}

/// <summary>
/// One entry of an area's element list: <c>VisitElement</c> (<c>0x0a060290</c>)'s shared header,
/// the element's keyframes, the widget's own body, and - for the four focusable wrappers - a
/// neighbour tail.
/// </summary>
public sealed class MgbElement : MgbRecord
{
    /// <summary>Type slot naming the *widget* class. The <c>Element</c> subclass that wraps it is
    /// derived, not stored - see <see cref="MgbSchema.WidgetWrapper"/>.</summary>
    public byte TypeSlot;

    public string WidgetTypeName = "Placeholder";

    public MgbUserData UserData = new();
    public MgbActionCaller Action = new();

    /// <summary>Inverted by the engine into <c>Element::SetVisible</c>.</summary>
    public bool Hidden;

    public bool IsDuplicatable;

    /// <summary>Read as a <c>u32</c>; only the low 3 bits are kept.</summary>
    public uint MaskMode;

    public List<MgbKeyframe> Keyframes = [];
    public MgbWidget Widget = new MgbPlaceholder();

    /// <summary>Present exactly when the wrapper is not plain <c>Element</c>.</summary>
    public MgbFocusableTail? Focusable;

    public string WrapperTypeName => MgbSchema.WidgetWrapper.GetValueOrDefault(WidgetTypeName, "Element");

    /// <summary>The concrete keyframe state class, decided by the widget's class alone.</summary>
    public string StateTypeName => MgbSchema.WidgetState.GetValueOrDefault(WidgetTypeName, "RectState");

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        using (c.Scope("USERDATA"))
        {
            UserData.Serialize(c, ctx);
        }
        Action.Serialize(c, ctx);
        c.Bool("HIDDEN", ref Hidden);
        c.Bool("ISDUPLICATABLE", ref IsDuplicatable);
        c.EnumU32("MASKMODE", ref MaskMode, MgbEnums.MaskMode);

        int n = Keyframes.Count;
        using (c.ListScope("KEYFRAMES", ref n))
        {
            if (c.IsReading)
            {
                Keyframes.Clear();
                for (int i = 0; i < n; i++)
                {
                    Keyframes.Add(new MgbKeyframe { StateTypeName = StateTypeName });
                }
            }
            foreach (MgbKeyframe keyframe in Keyframes)
            {
                keyframe.StateTypeName = StateTypeName;
                using (c.Item("Keyframe"))
                {
                    keyframe.Serialize(c, ctx);
                }
            }
        }

        if (c.IsReading)
        {
            Widget = MgbWidget.Create(WidgetTypeName);
        }
        using (c.Scope(WidgetTypeName))
        {
            Widget.Serialize(c, ctx);
        }

        if (MgbSchema.WrapperHasFocusableTail(WidgetTypeName))
        {
            Focusable ??= new MgbFocusableTail();
            using (c.Scope("FOCUSABLE"))
            {
                Focusable.Serialize(c, ctx);
            }
        }
        else if (c.IsReading)
        {
            Focusable = null;
        }
    }

    /// <summary>Reads or writes a list of elements, each prefixed by its own type slot.</summary>
    public static void SerializeElementList(IMgbCodec c, MgbContext ctx, List<MgbElement> elements)
        => MgbRecordHelpers.SlottedList(c, ctx, "Element", elements, e => e.TypeSlot, (slot, name) =>
        {
            if (!MgbSchema.IsWidgetType(name))
            {
                throw new MgbFormatException(
                    $"element type slot {slot} resolves to '{name ?? "<unnamed>"}', which is not one " +
                    $"of the 14 widget classes Factory::MakeElement can construct, at offset {c.Position - 1}");
            }
            return new MgbElement { TypeSlot = slot, WidgetTypeName = name! };
        });
}

/// <summary>One <c>DEFAULT_ELEMENT</c> entry of a page.</summary>
public sealed class MgbElementTag : MgbRecord
{
    public byte Controller;
    public uint Id;

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U8("CONTROLLER", ref Controller);
        c.NameId("ID", ref Id);
    }
}

/// <summary>
/// One entry of the package's top-level area list: <c>VisitArea</c> (<c>0x0a05f4b0</c>)'s shared
/// body plus the concrete subtype's tail.
/// </summary>
public sealed class MgbArea : MgbRecord
{
    public byte TypeSlot;

    /// <summary>One of <see cref="MgbSchema.AreaTypes"/>.</summary>
    public string TypeName = "Area";

    public MgbUserData UserData = new();
    public MgbActionCaller Action = new();

    /// <summary>The engine stores <c>1000 / FrameRate</c>.</summary>
    public uint FrameRate;

    public uint CurrentFrame;
    public List<MgbElement> Elements = [];

    /// <summary><c>STATICBOX</c>, in the same left/right/top/bottom order as
    /// <see cref="MgbRectState"/>.</summary>
    public ushort[] StaticBox = new ushort[4];

    // --- Page ---
    public List<MgbElementTag> DefaultElementTags = [];
    public bool SingleGlobalSelection;

    // --- Cursor: HOTSPOT, stored negated by the engine ---
    public ushort HotspotX;
    public ushort HotspotY;

    /// <summary>Button's six and CheckBox's twelve - the latter being Button's six plus six more
    /// for the checked state.</summary>
    public uint[] Timings = [];

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        using (c.Scope("USERDATA"))
        {
            UserData.Serialize(c, ctx);
        }
        Action.Serialize(c, ctx);
        c.U32("FRAMERATE", ref FrameRate);
        c.U32("CURRENTFRAME", ref CurrentFrame);
        MgbElement.SerializeElementList(c, ctx, Elements);
        c.U16Array("STATICBOX", StaticBox);

        switch (TypeName)
        {
            case "Area":
                break;
            case "Page":
                SerializeList(c, ctx, "DEFAULT_ELEMENTS", "DEFAULT_ELEMENT", DefaultElementTags);
                c.Bool("SINGLE_GLOBAL_SELECTION", ref SingleGlobalSelection);
                break;
            case "Cursor":
                c.U16("HOTSPOT.x", ref HotspotX);
                c.U16("HOTSPOT.y", ref HotspotY);
                break;
            case "Button":
            case "CheckBox":
                int count = TypeName == "Button" ? 6 : 12;
                if (c.IsReading || Timings.Length != count)
                {
                    Array.Resize(ref Timings, count);
                }
                c.U32Array("TIMINGS", Timings);
                break;
            default:
                throw new MgbFormatException($"'{TypeName}' is not an Area subtype");
        }
    }

    /// <summary>Reads or writes the package's area list, each entry prefixed by its type slot.</summary>
    public static void SerializeAreaList(IMgbCodec c, MgbContext ctx, List<MgbArea> areas)
        => MgbRecordHelpers.SlottedList(c, ctx, "Area", areas, a => a.TypeSlot, (slot, name) =>
        {
            if (!MgbSchema.IsAreaType(name))
            {
                throw new MgbFormatException(
                    $"area type slot {slot} resolves to '{name ?? "<unnamed>"}', which is not one of " +
                    $"the 5 classes Factory::MakeArea can construct, at offset {c.Position - 1}");
            }
            return new MgbArea { TypeSlot = slot, TypeName = name! };
        });
}
