using System.IO.Hashing;
using System.Text;

namespace JackAll.Tools.Mgb;

/// <summary>
/// The closed sets of classes the engine's three <c>Factory</c> dispatchers accept, and the fixed
/// maps between them.
/// </summary>
/// <remarks>
/// Each dispatcher is an ancestor walk over an <c>ObjectTypeInfo</c>: it tests the type and then its
/// bases against a fixed list, most-derived match wins, and a type matching nothing yields a null
/// object that the caller dereferences without a guard. So a type outside these sets cannot appear
/// in that slot in any file the game can actually load - which is why the reader treats an
/// unexpected type as a hard error rather than guessing a fallback shape, and why the editor can
/// offer exactly these sets and never produce a file the game rejects.
/// </remarks>
public static class MgbSchema
{
    /// <summary><c>Factory::MakeArea</c> (FarCry2_server <c>0x0a0480a0</c>). The package's top-level
    /// area list accepts only these.</summary>
    public static readonly string[] AreaTypes = ["Area", "Page", "Button", "Cursor", "CheckBox"];

    /// <summary><c>Factory::MakeElement</c> (<c>0x0a0481a0</c>): the 14 widget classes an area's
    /// element list accepts, mapped to the <c>Element</c> subclass that wraps each one. The wrapper
    /// decides whether a <see cref="MgbFocusableTail"/> follows the widget body.</summary>
    public static readonly Dictionary<string, string> WidgetWrapper = new(StringComparer.Ordinal)
    {
        ["Image"] = "Element",
        ["Text"] = "Element",
        ["RectShape"] = "Element",
        ["Placeholder"] = "Element",
        ["Window"] = "Element",
        ["AreaInstance"] = "Element",
        ["AutonomousAreaInstance"] = "Element",
        ["ButtonInstance"] = "Focusable",
        ["ListBox"] = "Focusable",
        ["EditBox"] = "Focusable",
        ["Slider"] = "Focusable",
        ["PageInstance"] = "PageFocusable",
        ["CheckBoxInstance"] = "Checkable",
        ["RadioButtonInstance"] = "Radioable",
    };

    /// <summary><c>Factory::MakeState</c> (<c>0x0a047c30</c>): the owning widget's class alone
    /// decides the concrete <c>State</c> subclass used by *every* keyframe on that element. This is
    /// a compile-time 1:1 map - nothing about it is per-instance or data-driven.</summary>
    public static readonly Dictionary<string, string> WidgetState = new(StringComparer.Ordinal)
    {
        ["Image"] = "ImageState",
        ["Text"] = "TextState",
        ["RectShape"] = "RectShapeState",
        ["Placeholder"] = "RectState",
        ["Window"] = "RectState",
        ["AreaInstance"] = "ScaleState",
        ["AutonomousAreaInstance"] = "ScaleState",
        ["ButtonInstance"] = "ScaleState",
        ["CheckBoxInstance"] = "ScaleState",
        ["RadioButtonInstance"] = "ScaleState",
        ["PageInstance"] = "ScaleState",
        ["ListBox"] = "ScaleState",
        ["EditBox"] = "ScaleState",
        ["Slider"] = "ScaleState",
    };

    /// <summary>The eight <c>ActionExecuter</c> subtypes that append
    /// <c>VisitActionExecuterEvent</c>'s named-event index table on top of the flat action list.
    /// Bare <c>ActionExecuter</c> - the ninth - does not.</summary>
    public static readonly HashSet<string> ActionExecuterEventTypes = new(StringComparer.Ordinal)
    {
        "ActionExecuterEvent", "ActionExecuterInputable", "ActionExecuterFocusable",
        "ActionExecuterPage", "ActionExecuterPageInstance", "ActionExecuterListbox",
        "ActionExecuterEditbox", "ActionExecuterSlider",
    };

    /// <summary>All nine, i.e. everything <c>Factory::MakeActionExecuter</c> can build.</summary>
    public static readonly HashSet<string> ActionExecuterTypes =
        [.. ActionExecuterEventTypes, "ActionExecuter"];

    public static bool IsAreaType(string? name) => name is not null && Array.IndexOf(AreaTypes, name) >= 0;
    public static bool IsWidgetType(string? name) => name is not null && WidgetWrapper.ContainsKey(name);

    /// <summary>Whether the wrapper <c>Factory::MakeElement</c> builds for this widget adds
    /// <c>VisitFocusable</c>'s neighbour tail. <c>PageFocusable</c>/<c>Checkable</c>/<c>Radioable</c>
    /// are pure forwards to <c>VisitFocusable</c>, so all four behave identically on the wire.</summary>
    public static bool WrapperHasFocusableTail(string widgetName)
        => WidgetWrapper.TryGetValue(widgetName, out string? w) && w != "Element";
}

/// <summary>
/// Resolves a package's type-table slots to class names.
/// </summary>
/// <remarks>
/// A file's type table holds raw <c>Id</c> values, and <c>magma::Id::Hash</c> is plain CRC-32
/// (IEEE, <c>0xEDB88320</c>) over the bare ASCII class name - no namespace, no mangling - so names
/// resolve from a static dictionary without needing the engine's registration order.
///
/// The off-by-one matters: <c>ReadHeader</c>'s fill loop runs slots <c>1 .. count-1</c>, so a body
/// type byte <c>B</c> names table entry <c>B-1</c>. Byte 0, and any slot whose id is 0, resolve
/// through the part of the remap array the constructor memsets to zero.
/// </remarks>
public sealed class MgbTypeTable
{
    private static readonly Dictionary<uint, string> NamesByHash = BuildNameDictionary();

    /// <summary>Raw ids as stored, index <c>i</c> being body type byte <c>i + 1</c>.</summary>
    public List<uint> RawIds { get; } = [];

    public static uint Hash(string className)
        => Crc32.HashToUInt32(Encoding.ASCII.GetBytes(className));

    /// <summary>The class name for a body type byte, or null if the slot is unassigned or its id
    /// matches no known name.</summary>
    public string? NameForSlot(byte slot)
    {
        int index = slot - 1;
        if (index < 0 || index >= RawIds.Count)
        {
            return null;
        }
        uint id = RawIds[index];
        return id != 0 && NamesByHash.TryGetValue(id, out string? name) ? name : null;
    }

    /// <summary>The body type byte that names <paramref name="className"/>, appending a table entry
    /// if the package doesn't already declare it.</summary>
    /// <remarks>
    /// Appending is only possible because the writer reserialises the whole package - the old
    /// byte-splicing editor could not grow the table, since inserting an entry shifts every offset
    /// after it. The ceiling is the header's single count byte: 254 entries. Real files use 167.
    /// </remarks>
    public byte SlotForName(string className)
    {
        uint id = Hash(className);
        int index = RawIds.IndexOf(id);
        if (index >= 0)
        {
            return (byte)(index + 1);
        }
        if (RawIds.Count >= 254)
        {
            throw new MgbFormatException(
                $"cannot declare '{className}': the type table is full (254 entries), which is all " +
                "the header's single count byte can address.");
        }
        RawIds.Add(id);
        return (byte)RawIds.Count;
    }

    /// <summary>Every class name this build knows how to resolve, for UI pickers.</summary>
    public static IReadOnlyCollection<string> KnownClassNames => NamesByHash.Values;

    private static Dictionary<uint, string> BuildNameDictionary()
    {
        string[] names =
        [
            // Widget hierarchy: the 14 Factory::MakeElement accepts, plus bases and
            // non-constructible members.
            "Image", "Text", "RectShape", "Placeholder", "Window", "AreaInstance",
            "AutonomousAreaInstance", "ButtonInstance", "CheckBoxInstance", "RadioButtonInstance",
            "PageInstance", "ListBox", "EditBox", "Slider",
            "Widget", "Element", "Focusable", "PageFocusable", "Checkable", "Radioable",
            "TextBase", "PixmapFont", "ExternalFont", "Font", "GlyphFont",
            // Area hierarchy.
            "Area", "Page", "Button", "CheckBox", "Cursor",
            // Keyframe / State hierarchy.
            "Keyframe", "State", "RotationState", "PosState", "ScaleState", "RectState",
            "TextBaseState", "TextState", "ImageState", "RectShapeState",
            // Action hierarchy.
            "ActionExecuter", "ActionExecuterEvent", "ActionExecuterInputable",
            "ActionExecuterFocusable", "ActionExecuterPage", "ActionExecuterPageInstance",
            "ActionExecuterListbox", "ActionExecuterEditbox", "ActionExecuterSlider",
            "Action", "ActionCaller", "ActionContinue", "ActionStop", "ActionPopPage",
            "ActionPushPage", "ActionGotoFrameIndex", "ActionGotoKeyFrame",
            // Package-level and infrastructure.
            "Package", "NamedObject", "UserData", "UserDataItem", "Material", "Texture",
            "FontFamily", "StringTable", "StringResource", "StringResourceExternalId",
            "FullLink", "AreaLink", "AreaLinkTags", "GenericObject", "GenericObjectTable",
            "EngineRoot", "EngineObject", "EngineObjectGroup", "AnonymousType", "Variant",
            "VariantContainer", "WindowSection", "StretchableWindowSection",
            "DisplayConfiguration", "BaseObject", "Acceptor", "IScrollable",
            // Handlers and timing strategies: registered, never Factory-constructible, but they do
            // appear as type-table entries and as AreaLink/Keyframe timing references.
            "Handler", "AreaHandler", "PageHandler", "DrawHandler", "EventHandler",
            "TimingStrategy", "TickTimingStrategy", "NoTimingStrategy", "SyncTimingStrategy",
            "EventTriggeredTimingStrategy", "TextScrollerPageHandler", "TextScrollerEventHandler",
            "TextScrollerDrawHandler",
            // Nomad (platform) subclasses seen in a live objecttypemanager::Register hook.
            "CTextureNomad", "CEditBoxNomad", "CActionSignalBase", "SpecificType<ClassType>",
            "SpecificType<void>", "CActionSignal<S>", "ActionManager", "ObjectTypeCollection",
        ];

        var map = new Dictionary<uint, string>(names.Length);
        foreach (string name in names)
        {
            map[Hash(name)] = name;
        }
        return map;
    }
}

/// <summary>Per-package state the records need while (de)serialising: chiefly the type table, since
/// every polymorphic slot resolves through it.</summary>
public sealed class MgbContext(MgbTypeTable types)
{
    public MgbTypeTable Types { get; } = types;
}
