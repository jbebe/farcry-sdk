using System.Xml.Linq;

namespace JackAll.Core.Mods;

/// <summary>
/// The control config files: one fragment per named section, a <c>&lt;Category&gt;</c> of
/// <c>defaultusercontrols.xml</c> or an <c>&lt;ActionMap&gt;</c> of an <c>inputactionmap*.xml</c>,
/// merged as a list whose items are each decided whole.
/// </summary>
public sealed class InputConfigContainerSplitter : XmlElementSplitter
{
    private const string NameAttribute = "name";

    public static InputConfigContainerSplitter Instance { get; } = new();

    /// <summary>Whether this file is the control list or one of the action maps.</summary>
    public static bool IsInputConfig(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Equals("defaultusercontrols.xml", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith("inputactionmap", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A section's id: its name, as a file.</summary>
    public static string IdOf(string sectionName) => FileIdOf(sectionName);

    protected override string IdHint => "it needs a name";

    protected override string? IdOf(XElement element) => RootIdOf(element, NameAttribute);

    /// <summary>A child's name, else the action map it imports, else its whole text.</summary>
    protected override string KeyOf(XElement child)
        => child.Name.LocalName.ToLowerInvariant() + "|"
            + ((string?)child.Attribute(NameAttribute)
                ?? (string?)child.Attribute("actionmap")
                ?? child.ToString(SaveOptions.DisableFormatting));

    protected override bool IsWhole(XElement child) => true;
}
