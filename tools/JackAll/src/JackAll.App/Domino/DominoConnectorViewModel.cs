using System.Windows;

namespace JackAll.App.Domino;

/// <summary>What a port carries, which is what decides how it and its wires are drawn.</summary>
public enum PortKind
{
    /// <summary>An execution pin - what fires next. Drawn as a solid stepped wire.</summary>
    Control,

    /// <summary>A typed value pin. Drawn as a thinner curve, colored by the value's type.</summary>
    Data,
}

/// <summary>
/// One port on one node. <see cref="Anchor"/> is written back by nodify (the connector reports where it
/// ended up on screen) and read by every connection attached to this port, which is why it must raise
/// change notifications - it is the only channel by which a wire learns where to draw itself.
/// </summary>
public sealed class DominoConnectorViewModel : DominoObservable
{
    private Point _anchor;
    private bool _isConnected;

    public DominoConnectorViewModel(string name, PortKind kind, string? type = null, bool delayed = false, bool declared = true)
    {
        Name = name;
        Kind = kind;
        Type = type;
        Delayed = delayed;
        Declared = declared;
    }

    /// <summary>The pin's identifier as the generated Lua spells it (`Greet_finished`).</summary>
    public string Name { get; }

    public PortKind Kind { get; }

    /// <summary>The declared value type (`Nomad|entity`, `Core|float`) for a data port; null for
    /// control ports and for sub-graph data ports, whose types aren't recoverable.</summary>
    public string? Type { get; }

    /// <summary>A `Delayed="true"` control-out fires on a later tick rather than synchronously.</summary>
    public bool Delayed { get; }

    /// <summary>False for a port that isn't in the node type's signature but is referenced by the graph
    /// anyway - shown so the wire still has somewhere to land instead of vanishing.</summary>
    public bool Declared { get; }

    /// <summary>What the port shows: the editor's own label when the twin supplied one, else the
    /// identifier.</summary>
    public string Title { get; init; } = string.Empty;

    public Point Anchor
    {
        get => _anchor;
        set => Set(ref _anchor, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => Set(ref _isConnected, value);
    }

    public string Tooltip => Kind == PortKind.Data
        ? $"{Name}{(Type is null ? "" : $"  :  {Type}")}"
        : $"{Name}{(Delayed ? "  (delayed)" : "")}";
}
