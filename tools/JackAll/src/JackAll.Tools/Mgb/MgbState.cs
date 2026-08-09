namespace JackAll.Tools.Mgb;

/// <summary>
/// <c>VisitState</c> (<c>0x0a05dc90</c>) - the root of the keyframe animation-state hierarchy.
/// </summary>
/// <remarks>
/// The hierarchy, with cumulative on-the-wire sizes:
/// <code>
/// State (8) -> RotationState (16) -+-> PosState  (20) -> ScaleState (28)
///                                  \-> RectState (24) -+-> TextBaseState (30) -> TextState (42)
///                                                      +-> ImageState (65)
///                                                      \-> RectShapeState (51)
/// </code>
/// <c>PosState</c> and <c>RectState</c> are siblings, not a chain: both write their own fields
/// starting at object offset <c>+0x24</c>, so <c>POSITION</c> x/y occupies the same storage as
/// <c>LEFT</c>/<c>RIGHT</c>.
/// </remarks>
public class MgbState : MgbRecord
{
    public uint InterpolationFlags;

    /// <summary>Packed ARGB, <c>0xAARRGGBB</c> (authored as <c>%d %d %d %d</c>, A first).</summary>
    public uint StateColor;

    /// <summary>The concrete class name, which the owning widget's class decides via
    /// <c>Factory::MakeState</c> - never the stream.</summary>
    public virtual string TypeName => "State";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.U32("INTERPOLATIONFLAGS", ref InterpolationFlags);
        c.ColorU32("STATECOLOR", ref StateColor);
    }

    /// <summary>Builds the concrete state a keyframe on <paramref name="stateTypeName"/>'s owner
    /// uses. The name comes from <see cref="MgbSchema.WidgetState"/>.</summary>
    public static MgbState Create(string stateTypeName) => stateTypeName switch
    {
        "State" => new MgbState(),
        "RotationState" => new MgbRotationState(),
        "PosState" => new MgbPosState(),
        "ScaleState" => new MgbScaleState(),
        "RectState" => new MgbRectState(),
        "TextBaseState" => new MgbTextBaseState(),
        "TextState" => new MgbTextState(),
        "ImageState" => new MgbImageState(),
        "RectShapeState" => new MgbRectShapeState(),
        _ => throw new MgbFormatException($"unknown keyframe state class '{stateTypeName}'"),
    };
}

/// <summary><c>VisitRotationState</c> (<c>0x0a060460</c>).</summary>
public class MgbRotationState : MgbState
{
    public uint RotationBits;
    public ushort OriginX;
    public ushort OriginY;

    public override string TypeName => "RotationState";

    public float Rotation
    {
        get => BitConverter.UInt32BitsToSingle(RotationBits);
        set => RotationBits = BitConverter.SingleToUInt32Bits(value);
    }

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.F32Bits("ROTATION", ref RotationBits);
        c.U16("ORIGIN.x", ref OriginX);
        c.U16("ORIGIN.y", ref OriginY);
    }
}

/// <summary><c>VisitPosState</c> (<c>0x0a060180</c>).</summary>
public class MgbPosState : MgbRotationState
{
    public ushort PositionX;
    public ushort PositionY;

    public override string TypeName => "PosState";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.U16("POSITION.x", ref PositionX);
        c.U16("POSITION.y", ref PositionY);
    }
}

/// <summary><c>VisitScaleState</c> (<c>0x0a05dcd0</c>).</summary>
public sealed class MgbScaleState : MgbPosState
{
    public uint ScaleXBits;
    public uint ScaleYBits;

    public override string TypeName => "ScaleState";

    public float ScaleX
    {
        get => BitConverter.UInt32BitsToSingle(ScaleXBits);
        set => ScaleXBits = BitConverter.SingleToUInt32Bits(value);
    }

    public float ScaleY
    {
        get => BitConverter.UInt32BitsToSingle(ScaleYBits);
        set => ScaleYBits = BitConverter.SingleToUInt32Bits(value);
    }

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.F32Bits("SCALEX", ref ScaleXBits);
        c.F32Bits("SCALEY", ref ScaleYBits);
    }
}

/// <summary><c>VisitRectState</c> (<c>0x0a05fc20</c>). Note the field order is left/right/top/bottom,
/// not the l/t/r/b anyone would guess.</summary>
public class MgbRectState : MgbRotationState
{
    public ushort Left;
    public ushort Right;
    public ushort Top;
    public ushort Bottom;

    public override string TypeName => "RectState";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.U16("LEFT", ref Left);
        c.U16("RIGHT", ref Right);
        c.U16("TOP", ref Top);
        c.U16("BOTTOM", ref Bottom);
    }
}

/// <summary><c>VisitTextBaseState</c> (<c>0x0a05dd20</c>).</summary>
public class MgbTextBaseState : MgbRectState
{
    public uint OffsetYBits;
    public ushort AbsOffsetY;

    public override string TypeName => "TextBaseState";

    public float OffsetY
    {
        get => BitConverter.UInt32BitsToSingle(OffsetYBits);
        set => OffsetYBits = BitConverter.SingleToUInt32Bits(value);
    }

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.F32Bits("OFFSETY", ref OffsetYBits);
        c.U16("ABSOFFSETY", ref AbsOffsetY);
    }
}

/// <summary><c>VisitTextState</c> (<c>0x0a05fb70</c>). <c>TEXTCOLOR</c> in the XML is the inherited
/// <see cref="MgbState.StateColor"/> under another name, not a field of its own.</summary>
public sealed class MgbTextState : MgbTextBaseState
{
    public uint ShadowColor;

    /// <summary>Read as a <c>u16</c>, stored by the engine as a float.</summary>
    public ushort Height;

    public byte ShadowOffsetX;
    public byte ShadowOffsetY;
    public ushort Leading;
    public ushort Tracking;

    public override string TypeName => "TextState";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.ColorU32("SHADOWCOLOR", ref ShadowColor);
        c.U16("HEIGHT", ref Height);
        c.U8("SHADOWOFFSETX", ref ShadowOffsetX);
        c.U8("SHADOWOFFSETY", ref ShadowOffsetY);
        c.U16("LEADING", ref Leading);
        c.U16("TRACKING", ref Tracking);
    }
}

/// <summary><c>VisitImageState</c> (<c>0x0a05fa20</c>).</summary>
public sealed class MgbImageState : MgbRectState
{
    public uint ShadowColor;
    public byte ShadowOffsetX;
    public byte ShadowOffsetY;
    public uint TilingXBits;
    public uint TilingYBits;
    public uint OffsetXBits;
    public uint OffsetYBits;
    public bool FlipHorizontal;
    public bool FlipVertical;
    public bool ActualSize;

    /// <summary><c>COLOR1</c>..<c>COLOR4</c>: the four corner colours of a gradient quad.</summary>
    public uint[] Colors = new uint[4];

    public override string TypeName => "ImageState";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.ColorU32("SHADOWCOLOR", ref ShadowColor);
        c.U8("SHADOWOFFSETX", ref ShadowOffsetX);
        c.U8("SHADOWOFFSETY", ref ShadowOffsetY);
        c.F32Bits("TILING.x", ref TilingXBits);
        c.F32Bits("TILING.y", ref TilingYBits);
        c.F32Bits("OFFSET.x", ref OffsetXBits);
        c.F32Bits("OFFSET.y", ref OffsetYBits);
        c.Bool("FLIPHORIZONTAL", ref FlipHorizontal);
        c.Bool("FLIPVERTICAL", ref FlipVertical);
        c.Bool("ACTUALSIZE", ref ActualSize);
        for (int i = 0; i < 4; i++)
        {
            c.ColorU32($"COLOR{i + 1}", ref Colors[i]);
        }
    }
}

/// <summary><c>VisitRectShapeState</c> (<c>0x0a05f950</c>).</summary>
public sealed class MgbRectShapeState : MgbRectState
{
    public byte OutlineWeight;
    public uint OutlineColor;

    /// <summary><c>FILLCOLOR1</c>..<c>FILLCOLOR4</c>.</summary>
    public uint[] FillColors = new uint[4];

    public uint ShadowColor;
    public byte ShadowOffsetX;
    public byte ShadowOffsetY;

    public override string TypeName => "RectShapeState";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        base.Serialize(c, ctx);
        c.U8("OUTLINEWEIGHT", ref OutlineWeight);
        c.ColorU32("OUTLINECOLOR", ref OutlineColor);
        for (int i = 0; i < 4; i++)
        {
            c.ColorU32($"FILLCOLOR{i + 1}", ref FillColors[i]);
        }
        c.ColorU32("SHADOWCOLOR", ref ShadowColor);
        c.U8("SHADOWOFFSETX", ref ShadowOffsetX);
        c.U8("SHADOWOFFSETY", ref ShadowOffsetY);
    }
}

/// <summary>
/// <c>VisitKeyframe</c> (<c>0x0a05ea90</c>): one animation keyframe on an element.
/// </summary>
public sealed class MgbKeyframe : MgbRecord
{
    public uint NameId;
    public MgbActionCaller Action = new();

    /// <summary>The frame index (<c>IDX</c>). Read as a <c>u32</c>, stored by the engine as a
    /// <c>u16</c>.</summary>
    public uint Idx;

    /// <summary>The easing curve, a plain <c>Util::GetType</c> group-0 value. Not a timing-strategy
    /// type id - that is <see cref="MgbAreaLink.TimingSlot"/>.</summary>
    public uint Interpolation;

    public MgbState State = new();

    /// <summary>Set by the owning element from <see cref="MgbSchema.WidgetState"/>; the wire
    /// carries no discriminator.</summary>
    public string StateTypeName = "RectState";

    public override void Serialize(IMgbCodec c, MgbContext ctx)
    {
        c.NameId("name", ref NameId);
        Action.Serialize(c, ctx);
        c.U32("IDX", ref Idx);
        c.EnumU32("INTERPOLATION", ref Interpolation, MgbEnums.Interpolation);
        if (c.IsReading)
        {
            State = MgbState.Create(StateTypeName);
        }
        using (c.Scope(StateTypeName))
        {
            State.Serialize(c, ctx);
        }
    }
}
