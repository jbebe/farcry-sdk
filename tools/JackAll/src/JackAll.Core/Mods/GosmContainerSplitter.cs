using System.Xml.Linq;

namespace JackAll.Core.Mods;

/// <summary>
/// A pawn state machine, <c>*.gosm.xml</c>: one fragment per root <c>Group</c>, <c>State</c> or
/// <c>Track</c> by its <c>FullName</c>, so two mods adding to one group both land.
/// </summary>
public sealed class GosmContainerSplitter : XmlElementSplitter
{
    public const string Suffix = ".gosm.xml";

    private const string NameAttribute = "FullName";

    /// <summary>The attributes a child is told apart by, in the order they are tried.</summary>
    private static readonly string[] KeyAttributes = [NameAttribute, "Path", "Name"];

    public static GosmContainerSplitter Instance { get; } = new();

    public static bool IsStateMachine(string path) => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>An element's id: its <c>FullName</c>, as a file.</summary>
    public static string IdOf(string fullName) => FileIdOf(fullName);

    protected override string IdHint => "it needs a FullName";

    protected override string? IdOf(XElement element) => RootIdOf(element, NameAttribute);

    /// <summary>A child's element name and its name, or a connection's target and signal, since one sink
    /// connects to a target on several signals.</summary>
    protected override string KeyOf(XElement child)
        => child.Name.LocalName == "Connection"
            ? $"Connection|{(string?)child.Attribute("Target")}|{(string?)child.Attribute("Signal")}"
            : NamedKey(child, KeyAttributes);
}
