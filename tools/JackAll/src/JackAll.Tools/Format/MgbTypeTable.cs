using System.IO.Hashing;

namespace JackAll.Tools.Format;

/// <summary>
/// Known Magma UI class names, keyed by CRC32(name) - the same hash the engine's own
/// <c>magma::Id::Hash</c> uses to build a .mgb file's type table (plain CRC-32/ISO-HDLC of the raw
/// ASCII class name - no namespace, no mangling). See reverse/dunia/mgb_format.md, "Type-table IDs
/// are CRC32(ClassName)".
/// </summary>
/// <remarks>
/// Cross-checked against one real sample file (options.mgb)'s 128 non-zero type-table entries: 91
/// (71%) matched on the first pass; a second pass (2026-07-31) cross-referenced ~190 real RTTI class
/// names recovered from Dunia.dll (rather than hand-guessed candidates) and resolved ~40 more,
/// including the entire <c>ActionExecuter</c> family and the <c>Action</c> opcode set - see the doc's
/// "Additional matches found via RTTI cross-reference" section. A third pass (2026-08-02) added 14
/// classes confirmed via a live <c>Register()</c> hook in an earlier session but never actually added
/// to this dictionary until now (a previous-session omission, not a new finding) - including the
/// <c>Handler</c> family, which turned out to be a real, previously-unreachable blocker once other
/// bugs were fixed (see the doc's "Major correctness pass" note). 92/128 entries in this same sample
/// file now resolve. One remaining entry (<c>0x86F001E3</c>) is the single biggest real blocker (13/21
/// shipped files) and survived every pass so far - it likely isn't a plain top-level class name at
/// all, see the doc's Unknowns section for the live-instrumentation plan to identify it.
/// </remarks>
public static class MgbTypeTable
{
    private static readonly string[] KnownClassNames =
    [
        "RectShape", "Text", "Image", "RectShapeState", "TextBase", "ImageState",
        "ListBox", "TextBaseState", "Window", "EditBox", "TextState", "Slider",
        "Placeholder", "AreaInstance", "AutonomousAreaInstance", "ButtonInstance",
        "CheckBoxInstance", "RadioButtonInstance", "PageInstance", "Area", "Page",
        "Button", "CheckBox", "Cursor", "Element", "Keyframe", "State",
        "RotationState", "PosState", "ScaleState", "RectState", "Focusable", "UserData",
        "NamedObject", "ActionCaller", "Widget", "Package", "EngineRoot", "Font",
        "FontFamily", "StringTable", "Material", "AnonymousType",
        // Added 2026-07-31 via RTTI cross-reference (see MgbTypeTable's own remarks above).
        "Acceptor", "EngineObject", "IScrollable", "AreaLink", "PageFocusable", "Checkable",
        "Radioable", "GlyphFont", "StringResource", "PixmapFont", "EngineObjectGroup",
        "DisplayConfiguration", "Action", "Texture", "FullLink", "WindowSection",
        "ActionExecuter", "UserDataItem", "StretchableWindowSection", "ActionExecuterEvent",
        "ActionExecuterInputable", "ActionExecuterFocusable", "ActionExecuterEditbox",
        "ActionExecuterListbox", "ActionExecuterPage", "ActionExecuterPageInstance",
        "ActionExecuterSlider", "AreaHandler", "PageHandler", "DrawHandler", "GenericObject",
        "EventTriggeredTimingStrategy", "TickTimingStrategy", "EventHandler", "TimingStrategy",
        "ActionContinue", "ActionStop", "ActionPopPage", "ActionPushPage",
        "ActionGotoFrameIndex", "ActionGotoKeyFrame",
        // Added 2026-08-02 via a live Register() hook (see the doc's "Additional matches found via
        // live Register hook" section) - confirmed real class names, but never actually added to this
        // dictionary until now (a previous-session omission, not a new finding this session).
        "BaseObject", "SpecificType<ClassType>", "SpecificType<void>", "CActionSignalBase",
        "CActionSignal<S>", "CTextureNomad", "CEditBoxNomad", "Handler", "SyncTimingStrategy",
        "NoTimingStrategy", "ExternalFont", "TextScrollerPageHandler", "TextScrollerEventHandler",
        "TextScrollerDrawHandler",
    ];

    private static readonly Dictionary<uint, string> ByCrc32 = BuildLookup();

    public static string? Resolve(uint crc32Id) => ByCrc32.GetValueOrDefault(crc32Id);

    /// <summary>The same <c>CRC32(ClassName)</c> this table itself is keyed by - exposed for writer
    /// code that needs to build a type table entry or a raw action-type-hash for a class name (e.g.
    /// <see cref="MgbFileBuilder"/>), not just resolve one that's already in a file.</summary>
    public static uint ComputeHash(string asciiName) => Compute(asciiName);

    private static Dictionary<uint, string> BuildLookup()
    {
        var map = new Dictionary<uint, string>(KnownClassNames.Length);
        foreach (string name in KnownClassNames)
        {
            map[Compute(name)] = name;
        }
        return map;
    }

    private static uint Compute(string asciiName)
    {
        Span<byte> bytes = stackalloc byte[asciiName.Length];
        for (int i = 0; i < asciiName.Length; i++)
        {
            bytes[i] = (byte)asciiName[i];
        }
        return Crc32.HashToUInt32(bytes);
    }
}
