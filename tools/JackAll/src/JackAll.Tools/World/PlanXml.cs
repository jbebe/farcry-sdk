using System.Globalization;
using System.Xml.Linq;

namespace JackAll.Tools.World;

/// <summary>The attributes of a world's plan files, read the same way by every plan.</summary>
internal static class PlanXml
{
    public static string Required(XElement element, string attribute)
        => (string?)element.Attribute(attribute)
           ?? throw new InvalidOperationException($"A {element.Name} needs {attribute}.");

    public static float Number(XElement element, string attribute, float? fallback = null)
        => element.Attribute(attribute) is { } value
            ? float.Parse(value.Value, CultureInfo.InvariantCulture)
            : fallback ?? throw new InvalidOperationException($"A {element.Name} needs {attribute}.");
}
