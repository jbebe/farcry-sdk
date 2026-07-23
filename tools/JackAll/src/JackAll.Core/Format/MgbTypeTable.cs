using System.IO.Hashing;

namespace JackAll.Core.Format;

/// <summary>
/// Known Magma UI class names, keyed by CRC32(name) - the same hash the engine's own
/// <c>magma::Id::Hash</c> uses to build a .mgb file's type table (plain CRC-32/ISO-HDLC of the raw
/// ASCII class name - no namespace, no mangling). See reverse/dunia/mgb_format.md, "Type-table IDs
/// are CRC32(ClassName)".
/// </summary>
/// <remarks>
/// Not exhaustive: cross-checked against one real sample file's 128 non-zero type-table entries, 91
/// (71%) matched a name here. The rest are real engine classes (an <c>ActionExecuter</c> family and
/// assorted infrastructure types were confirmed present in the binary) whose exact literal spelling
/// wasn't pinned down precisely enough to hash with confidence - see the doc for the full list of
/// names still missing from this table.
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
    ];

    private static readonly Dictionary<uint, string> ByCrc32 = BuildLookup();

    public static string? Resolve(uint crc32Id) => ByCrc32.GetValueOrDefault(crc32Id);

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
