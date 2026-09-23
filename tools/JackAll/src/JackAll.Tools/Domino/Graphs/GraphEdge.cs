namespace JackAll.Tools.Domino.Graphs;

public enum EdgeTarget
{
    /// <summary>Wired to another reconstructed node's named control-in pin.</summary>
    Node,

    /// <summary>Wired to this graph's own exposed control-out pin (relevant when the graph is itself
    /// used as a sub-box by a parent graph) - a `self:PinName();` fire reached from here.</summary>
    GraphExit,

    /// <summary>`Box.PinName = DummyFunction;` - the pin exists but was never connected in the editor.</summary>
    Unwired,

    /// <summary>The wired handler function (and anything it calls) never fires anything further - a pure
    /// data/field-set tail with no downstream box or exposed pin.</summary>
    DeadEnd,
}

/// <summary>
/// One reconstructed control connection: a box's control-out pin, or one of the graph's own control-ins
/// (<see cref="FromEntry"/>, with <see cref="SourcePin"/> naming it), wired to whatever runs next.
/// <see cref="Index"/> is the slot of a `Dynamic="True"` control-out wired as `Box.PinName[N] = ...`, and
/// <see cref="TargetIndex"/> the slot of a dynamic control-in fired as `Box._type.Pin(Box, N)`.
/// </summary>
public sealed record GraphEdge(
    string? SourceNodeId,
    string SourcePin,
    int? Index,
    EdgeTarget Target,
    string? TargetNodeId,
    string? TargetPin,
    string? GraphExitPin)
{
    public int? TargetIndex { get; init; }

    /// <summary>The function whose statement fires the target, and which fire in it this is - the
    /// order a handler fires its targets in, and the key a debug twin's trace is filed under.</summary>
    public string? FiredIn { get; init; }

    public int? FireOrdinal { get; init; }

    public bool FromEntry => SourceNodeId is null;
}
