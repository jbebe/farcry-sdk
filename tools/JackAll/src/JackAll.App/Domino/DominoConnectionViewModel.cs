namespace JackAll.App.Domino;

/// <summary>One wire between two ports. Both endpoints are always real connectors - a graph-boundary
/// connection gets a <see cref="NodeRole.Boundary"/> node to attach to rather than a dangling end, so
/// nodify always has two anchors to draw between.</summary>
public sealed class DominoConnectionViewModel
{
    public required DominoConnectorViewModel Source { get; init; }
    public required DominoConnectorViewModel Target { get; init; }
    public required PortKind Kind { get; init; }

    /// <summary>True for a data wire whose producer couldn't be pinned to one box - several handlers
    /// write the variable it travels through, so every candidate is drawn and all of them are marked.
    /// </summary>
    public bool Ambiguous { get; init; }

    /// <summary>The graph variable a data value travelled through, when it went by way of one. Shown as
    /// the wire's label, because "which variable is this" is the question you ask looking at it.</summary>
    public string? Label { get; init; }

    /// <summary>How many interchangeable occurrences of this same operation write the variable - more
    /// than one when a mission repeats a sequence per branch. The wire points at the nearest.</summary>
    public int SourceOccurrences { get; init; } = 1;

    public string Tooltip => Kind == PortKind.Control
        ? $"{Source.Name} → {Target.Name}"
        : $"{Source.Name} → {Target.Name}"
          + (Label is null ? "" : $"\nvia self.{Label}")
          + (Ambiguous ? "\nProducer is uncertain: several different boxes write this variable." : "")
          + (SourceOccurrences > 1 ? $"\nWritten by {SourceOccurrences} interchangeable occurrences of this same operation; the nearest is shown." : "");
}
