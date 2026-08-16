namespace JackAll.Tools.World;

/// <summary>A loaded single-player world: every sector document plus the flattened entity pool.</summary>
public sealed class Fc2World
{
    /// <summary>"world1" or "world2".</summary>
    public required string Name { get; init; }

    public required IReadOnlyDictionary<int, WorldSectorDocument> SectorsById { get; init; }

    /// <summary>The live entity pool. Mutated only through the editor session, which keeps the
    /// picking index, marker stream and dirty set in step with it.</summary>
    public required List<WorldEntity> Entities { get; init; }
}
