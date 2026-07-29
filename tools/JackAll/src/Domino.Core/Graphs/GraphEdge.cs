namespace Domino.Core.Graphs;

public enum EdgeTarget
{
    /// <summary>Wired to another reconstructed node's named control-in pin.</summary>
    Node,

    /// <summary>Wired to this graph's own exposed control-out pin (relevant when the graph is itself
    /// used as a sub-box by a parent graph) - a `self:PinName();` fire reached from here.</summary>
    GraphExit,

    /// <summary>`Box.PinName = DummyFunction;` - the pin exists but was never connected in the editor.</summary>
    Unwired,

    /// <summary>The wired handler function (and anything it redirects through) never fires anything
    /// further - a pure data/field-set tail with no downstream box or exposed pin.</summary>
    DeadEnd,
}

/// <summary>One reconstructed connection: a box's control-out pin wired to whatever runs next.
/// <see cref="Index"/> is set for a `Dynamic="True"` pin wired as `Box.PinName[N] = ...`.</summary>
public sealed record GraphEdge(
    string SourceNodeId,
    string SourcePin,
    int? Index,
    EdgeTarget Target,
    string? TargetNodeId,
    string? TargetPin,
    string? GraphExitPin);
