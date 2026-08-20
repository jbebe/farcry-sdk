using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace JackAll.Tools.World;

/// <summary>
/// The authored atmosphere from a world descriptor's <c>&lt;Environment&gt;</c> block - the part of
/// it that ships as literal values rather than as GUID references into the managers file.
/// </summary>
/// <remarks>
/// <code>
/// &lt;Fog Color="202,219,230" Start="0" End="400" FogAmount="0.8" /&gt;
/// &lt;Camera ViewDistance="1024" /&gt;
/// </code>
/// Two traps this parser is shaped around: colours in this block are 0-255 integers while the
/// <c>&lt;Layer&gt;</c> colours in the same file are 0-1 floats, and sibling elements carry literal
/// <c>f</c> suffixes (<c>Start="500.0f"</c>) that a plain <c>float.Parse</c> rejects.
/// </remarks>
public sealed record WorldEnvironment(
    Vector3 FogColour, float FogStart, float FogEnd, float FogAmount, float ViewDistance)
{
    /// <summary>The values every retail world examined ships; what a world with no readable
    /// descriptor falls back to.</summary>
    public static WorldEnvironment Default { get; } = new(
        new Vector3(202f / 255f, 219f / 255f, 230f / 255f),
        FogStart: 0f, FogEnd: 400f, FogAmount: 0.8f, ViewDistance: 1024f);

    public static WorldEnvironment Load(string mapName, Func<string, byte[]?> readByPath)
        => WorldDescriptor.TryLoadRoot(mapName, readByPath) is { } root ? Read(root) : Default;

    /// <summary>Reads the block off an already-parsed descriptor root; missing pieces keep their
    /// defaults individually, so a partial block degrades field by field rather than whole.</summary>
    public static WorldEnvironment Read(XElement root)
    {
        XElement? environment = root.Element("Environment");
        XElement? fog = environment?.Element("Fog");
        XElement? camera = environment?.Element("Camera");

        return Default with
        {
            FogColour = Colour(fog?.Attribute("Color")?.Value) ?? Default.FogColour,
            FogStart = Number(fog?.Attribute("Start")?.Value) ?? Default.FogStart,
            FogEnd = Number(fog?.Attribute("End")?.Value) ?? Default.FogEnd,
            FogAmount = Number(fog?.Attribute("FogAmount")?.Value) ?? Default.FogAmount,
            ViewDistance = Number(camera?.Attribute("ViewDistance")?.Value) ?? Default.ViewDistance,
        };
    }

    /// <summary>A descriptor number, shed of the <c>f</c> suffix the cooker leaves on some.</summary>
    private static float? Number(string? text)
        => text is not null
            && float.TryParse(text.TrimEnd('f', 'F'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value)
            ? value
            : null;

    /// <summary>A <c>"r,g,b"</c> byte triple, normalized to 0-1.</summary>
    private static Vector3? Colour(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string[] parts = text.Split(',');
        if (parts.Length < 3)
        {
            return null;
        }

        var colour = new Vector3();
        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float channel))
            {
                return null;
            }
            colour[i] = channel / 255f;
        }
        return colour;
    }
}
