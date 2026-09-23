namespace JackAll.Tools.Domino.Graphs;

/// <summary>Where a data value entering a box's data-in parameter came from.</summary>
public enum DataEdgeKind
{
    /// <summary>Another box's data-out pin, reached either directly (`self[14].Entity = self[8].ObjectEntity;`)
    /// or, far more commonly, by way of a graph-level variable.</summary>
    NodeToNode,

    /// <summary>A graph-level variable nothing in this graph ever produces - so it is this graph's own
    /// data input, supplied by whichever parent graph uses it as a sub-box.</summary>
    GraphInput,
}

/// <summary>
/// One reconstructed data connection: some box's data-out pin feeding some box's data-in parameter.
///
/// The generated code almost never wires a box straight to a box (20 places in the whole corpus). It
/// routes through a graph-level variable instead - `self.BuddyPawn = self[29].SpawnedBuddy;` in one
/// handler, `self[18].Pawn = self.BuddyPawn;` in another - so <see cref="ViaVariable"/> names the field
/// the value travelled through, and is null only for the rare direct form.
///
/// <see cref="Ambiguous"/> marks an edge whose producer could not be pinned down: several boxes write the
/// same variable and no control path from any of them arrives here. Every candidate gets its own edge
/// rather than the resolver presenting a guess as fact.
///
/// <see cref="SourceOccurrences"/> above 1 is not ambiguity: that many writers each reach the consumer on
/// their own control path - four `GetLocalPlayer` boxes feeding `self.Player`, one per story variant - and
/// which one supplied the value depends on the path taken.
/// </summary>
public sealed record DataEdge(
    string? SourceNodeId,
    string? SourcePin,
    string TargetNodeId,
    string TargetPin,
    string? ViaVariable,
    DataEdgeKind Kind,
    bool Ambiguous)
{
    /// <summary>How many writers reach the consumer through the same variable, this one included.</summary>
    public int SourceOccurrences { get; init; } = 1;
}
