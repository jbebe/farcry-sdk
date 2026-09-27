using System.Xml.Linq;

namespace JackAll.Tools.Ai;

/// <summary>A wire from one of a node's anchors, exits or events to another node's anchor.</summary>
public sealed record AiLink(string Kind, string From, AiNode Target, string TargetAnchor);

/// <summary>A brain's filter: while the C++ brain reports this behaviour, <see cref="Target"/> runs.</summary>
public sealed record AiSelectable(string Filter, AiNode Target);

/// <summary>One instance of a <c>BlackBox.AI</c> workspace: a brain, plan, scanner or task.</summary>
public sealed class AiNode(XElement element)
{
    public XElement Element { get; } = element;

    public string Name { get; } = (string)element.Attribute("Name")!;

    /// <summary>The last segment of <see cref="Name"/>, which is what the designers named it.</summary>
    public string ShortName => Name[(Name.LastIndexOf('/') + 1)..];

    public string Class { get; } = (string?)element.Attribute("Class") ?? "";

    /// <summary>Brain, Plan, Scanner or Task.</summary>
    public string Kind => Element.Name.LocalName;

    public bool Looping => (string?)Element.Attribute("Looping") == "1";

    public List<AiNode> Children { get; } = [];

    public List<AiNode> Parents { get; } = [];

    public List<AiSelectable> Selectables { get; } = [];

    public List<AiLink> Links { get; } = [];
}

/// <summary>
/// A brain workspace's source as a navigable graph. Nodes wrap the source elements, so editing a
/// parameter edits the document <see cref="AiWorkspacePacker"/> compiles.
/// </summary>
public sealed class AiBrainGraph
{
    private readonly Dictionary<string, AiNode> _byName = new(StringComparer.Ordinal);

    public AiBrainGraph(XElement source)
    {
        Source = source;
        foreach (XElement element in source.Elements())
        {
            var node = new AiNode(element);
            _byName.TryAdd(node.Name, node);
        }

        var selected = new HashSet<AiNode>();
        foreach (AiNode node in _byName.Values)
        {
            foreach (XElement child in node.Element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Add" when Find((string?)child.Attribute("Task")) is { } added:
                        node.Children.Add(added);
                        added.Parents.Add(node);
                        break;
                    case "Selectable" when Find((string?)child.Attribute("Task")) is { } target:
                        node.Selectables.Add(new AiSelectable((string)child.Attribute("Filter")!, target));
                        selected.Add(target);
                        break;
                    case "Anchor" or "Exit" or "Event":
                        foreach (XElement connection in child.Elements("Connection"))
                        {
                            if (Find((string?)connection.Attribute("Target")) is { } target)
                            {
                                node.Links.Add(new AiLink(child.Name.LocalName, (string)child.Attribute("Name")!, target,
                                    (string?)connection.Attribute("TargetAnchor") ?? ""));
                            }
                        }
                        break;
                }
            }
        }

        Roots = [.. _byName.Values.Where(n => n.Kind == "Brain" && n.Parents.Count == 0 && !selected.Contains(n))];
    }

    public XElement Source { get; }

    public IEnumerable<AiNode> Nodes => _byName.Values;

    public int Count => _byName.Count;

    /// <summary>The top-level brains: those no plan adds and no other brain selects.</summary>
    public IReadOnlyList<AiNode> Roots { get; }

    public AiNode? Find(string? name) => name is not null && _byName.TryGetValue(name, out AiNode? node) ? node : null;
}
