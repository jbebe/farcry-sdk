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
/// <see cref="Ambiguous"/> marks an edge whose producer could not be pinned down to one box: several
/// genuinely different boxes write the same variable, and nothing in the flattened script says which one
/// ran. Every candidate gets its own edge rather than the resolver picking one and presenting a guess as
/// fact.
///
/// <see cref="RepeatedSource"/> is the far more common near-miss, and deliberately not treated as
/// ambiguity: the candidates are all the same operation repeated - four `GetLocalPlayer` occurrences
/// feeding `self.Player`, one per branch of a mission that runs four story variants. Which occurrence
/// ran depends on the path taken, but they compute the same thing from the same node type's same pin, so
/// one edge from the nearest of them states the provenance correctly instead of drawing four
/// interchangeable wires into every consumer.
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
    /// <summary>How many interchangeable occurrences of this same operation write the variable; 1 for an
    /// ordinary edge.</summary>
    public int SourceOccurrences { get; init; } = 1;

    public bool RepeatedSource => SourceOccurrences > 1;
}
