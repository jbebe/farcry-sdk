using System.Globalization;
using System.Xml.Linq;

namespace JackAll.Core.Format.Mgb;

/// <summary>
/// The <c>POOLCOUNTS</c> a package needs: how many objects of each pooled kind it holds.
/// </summary>
/// <remarks>
/// Counted over the package's XML form, because the codec that renders it is already a complete walk
/// of every record. Every retail package that sets its counts sets exactly these; a pool no retail
/// package uses has no known kind, counts zero here, and so is left as found. See
/// docs/docs/file-formats/mgb.md.
/// </remarks>
public static class MgbPools
{
    public const int Count = 65;

    public const string Attribute = "POOLCOUNTS";

    /// <summary>The pool each kind of object is counted in, keyed <c>&lt;scope&gt;:&lt;class&gt;</c>.</summary>
    private static readonly Dictionary<string, int> PoolOf = new(StringComparer.Ordinal)
    {
        ["area:Area"] = 0,
        ["area:Page"] = 1,
        ["area:Button"] = 2,
        ["area:CheckBox"] = 3,
        ["area:Cursor"] = 4,
        ["wrapper:Element"] = 5,
        ["wrapper:Focusable"] = 6,
        ["wrapper:Checkable"] = 7,
        ["wrapper:PageFocusable"] = 51,
        ["state:ScaleState"] = 11,
        ["state:RectState"] = 12,
        ["state:TextState"] = 13,
        ["state:ImageState"] = 15,
        ["state:RectShapeState"] = 16,
        ["widget:Image"] = 17,
        ["widget:Text"] = 18,
        ["widget:RectShape"] = 20,
        ["widget:EditBox"] = 21,
        ["widget:ListBox"] = 22,
        ["widget:Slider"] = 23,
        ["widget:Placeholder"] = 24,
        ["widget:AreaInstance"] = 26,
        ["widget:ButtonInstance"] = 28,
        ["widget:CheckBoxInstance"] = 29,
        ["widget:PageInstance"] = 50,
        ["Keyframe"] = 31,
        ["STRING"] = 49,
        ["USERDATA"] = 52,
        ["PROPERTY"] = 54,
        ["PROPERTY:link"] = 53,
        ["executer:ActionExecuter"] = 55,
        ["executer:ActionExecuterFocusable"] = 58,
        ["executer:ActionExecuterPage"] = 59,
        ["executer:ActionExecuterEditbox"] = 60,
        ["executer:ActionExecuterListbox"] = 61,
        ["executer:ActionExecuterPageInstance"] = 62,
        ["executer:ActionExecuterSlider"] = 63,
        ["EVENT"] = 64,
    };

    private static readonly HashSet<string> StateClasses = [.. MgbSchema.WidgetState.Values];

    private static readonly HashSet<string> LinkTags =
    [
        .. new[] { MgbProperty.TagFullLinkA, MgbProperty.TagFullLinkB, MgbProperty.TagFullLinkC }
            .Select(tag => tag.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>The counts <paramref name="package"/> needs.</summary>
    public static uint[] Of(XElement package)
    {
        var counts = new uint[Count];
        foreach (XElement area in package.Element("CHILDREN")?.Elements() ?? [])
        {
            Add("area:" + (string?)area.Attribute("type"));
        }

        foreach (XElement node in package.Descendants())
        {
            string name = node.Name.LocalName;
            switch (name)
            {
                case "Element" when (string?)node.Attribute("type") is { } widget:
                    Add("widget:" + widget);
                    Add("wrapper:" + MgbSchema.WidgetWrapper.GetValueOrDefault(widget, "Element"));
                    // An element holds a live state of its own besides one per keyframe.
                    Add("state:" + MgbSchema.WidgetState.GetValueOrDefault(widget, "RectState"));
                    break;
                case "USERDATA" when node.Element("PROPERTIES")?.HasElements == true:
                case "Keyframe" or "STRING" or "EVENT":
                    Add(name);
                    break;
                case "PROPERTY":
                    Add(name);
                    if (LinkTags.Contains((string?)node.Attribute("type") ?? ""))
                    {
                        Add("PROPERTY:link");
                    }
                    break;
                case "ACTIONEXECUTER":
                    Add("executer:" + (string?)node.Attribute("type"));
                    break;
                default:
                    if (StateClasses.Contains(name))
                    {
                        Add("state:" + name);
                    }
                    break;
            }
        }

        return counts;

        void Add(string kind)
        {
            if (PoolOf.TryGetValue(kind, out int pool))
            {
                counts[pool]++;
            }
        }
    }

    public static uint[] Of(MgbPackage package) => Of(MgbXml.ToElement(package));

    /// <summary>Raises every pool of a package document to what it holds. None is lowered: headroom a
    /// package already carries is the author's.</summary>
    public static void Raise(XElement package)
    {
        uint[] declared = [.. MgbXmlValue.Tokens((string?)package.Attribute(Attribute) ?? "").Select(MgbXmlValue.ParseInteger)];
        if (declared.Length != Count)
        {
            throw new MgbFormatException($"{Attribute} holds {declared.Length} counts, not {Count}");
        }

        uint[] needed = Of(package);
        package.SetAttributeValue(Attribute, string.Join(' ', declared.Select((d, i) => MgbXmlValue.Integer(Math.Max(d, needed[i])))));
    }
}
