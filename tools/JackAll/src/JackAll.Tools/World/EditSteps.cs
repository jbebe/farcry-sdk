using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>Where an entity stood and faced.</summary>
public readonly record struct Placement(Vector3 Position, Vector3 Angles)
{
    public static Placement Of(WorldEntity entity) => new(entity.Position!.Value, entity.Angles);
}

/// <summary>Entities moved or turned, one drag or one typed transform.</summary>
/// <param name="mergeKey">Steps with the same non-null key on the same entity merge, so typing into a
/// transform field is one step; a gizmo drag passes null.</param>
public sealed class MoveStep(
    WorldEditSession session, IReadOnlyDictionary<WorldEntity, (Placement Before, Placement After)> moves,
    string? mergeKey = null) : IEditStep
{
    private IReadOnlyDictionary<WorldEntity, (Placement Before, Placement After)> _moves = moves;

    public string Label => _moves.Count == 1 ? $"Move {_moves.Keys.First().Name}" : $"Move {_moves.Count} entities";

    public IReadOnlyCollection<WorldEntity> Entities => [.. _moves.Keys];

    public bool ChangesMembership => false;

    public void Undo() => Apply(m => m.Before);

    public void Redo() => Apply(m => m.After);

    public bool TryMerge(IEditStep next)
    {
        if (mergeKey is null || next is not MoveStep other || other.MergeKey != mergeKey
            || _moves.Count != 1 || other._moves.Count != 1 || !_moves.Keys.SequenceEqual(other._moves.Keys))
        {
            return false;
        }

        WorldEntity entity = _moves.Keys.First();
        _moves = new Dictionary<WorldEntity, (Placement, Placement)> { [entity] = (_moves[entity].Before, other._moves[entity].After) };
        return true;
    }

    private string? MergeKey => mergeKey;

    private void Apply(Func<(Placement Before, Placement After), Placement> pick)
    {
        foreach ((WorldEntity entity, (Placement, Placement) move) in _moves)
        {
            Placement placement = pick(move);
            entity.Position = placement.Position;
            entity.Angles = placement.Angles;
            session.Moved(entity);
        }
    }
}

/// <summary>A change to an entity's node: a field, a component added or removed, a handle dragged.</summary>
public sealed class NodeEditStep(WorldEditSession session, WorldEntity entity, FcbObject before, FcbObject after, string label)
    : IEditStep
{
    private FcbObject _after = after;

    /// <summary>The one value the edit changed, or null when it changed more or changed structure.</summary>
    private readonly string? _changed = SoleChange(before, after);

    public string Label => label;

    public IReadOnlyCollection<WorldEntity> Entities => [entity];

    public bool ChangesMembership => false;

    public void Undo() => Apply(before);

    public void Redo() => Apply(_after);

    public bool TryMerge(IEditStep next)
    {
        if (next is not NodeEditStep other || other.Target != entity || _changed is null || other._changed != _changed)
        {
            return false;
        }
        _after = other._after;
        return true;
    }

    private WorldEntity Target => entity;

    private void Apply(FcbObject state)
    {
        session.EditableNode(entity).Overwrite(state);
        session.Edited(entity);
    }

    /// <summary>The path of the single value that differs between the two trees, or null.</summary>
    private static string? SoleChange(FcbObject a, FcbObject b)
    {
        var found = new List<string>();
        Collect(a, b, "", found);
        return found.Count == 1 ? found[0] : null;
    }

    private static void Collect(FcbObject a, FcbObject b, string path, List<string> found)
    {
        if (found.Count > 1)
        {
            return;
        }
        if (a.TypeHash != b.TypeHash || a.Children.Count != b.Children.Count || a.Values.Count != b.Values.Count)
        {
            found.AddRange([path, path]);
            return;
        }
        foreach ((uint hash, byte[] value) in a.Values)
        {
            if (!b.Values.TryGetValue(hash, out byte[]? other))
            {
                found.AddRange([path, path]);
                return;
            }
            if (!value.AsSpan().SequenceEqual(other))
            {
                found.Add($"{path}/{hash:X8}");
            }
        }
        for (int i = 0; i < a.Children.Count; i++)
        {
            Collect(a.Children[i], b.Children[i], $"{path}/{i}", found);
        }
    }
}

/// <summary>Entities added or deleted. Undoing one is doing the other, so this one step toggles.</summary>
public sealed class PresenceStep : IEditStep
{
    private readonly WorldEditSession _session;
    private readonly IReadOnlyList<WorldEntity> _entities;
    private readonly string _label;

    /// <summary>What restores the entities while they are gone; empty while they are present.</summary>
    private IReadOnlyList<DeletedRecord> _records;

    private PresenceStep(WorldEditSession session, IReadOnlyList<WorldEntity> entities, IReadOnlyList<DeletedRecord> records, string label)
    {
        _session = session;
        _entities = entities;
        _records = records;
        _label = label;
    }

    public static PresenceStep Added(WorldEditSession session, IReadOnlyList<WorldEntity> entities)
        => new(session, entities, [], entities.Count == 1 ? $"Add {entities[0].Name}" : $"Add {entities.Count} entities");

    public static PresenceStep Deleted(WorldEditSession session, IReadOnlyList<DeletedRecord> records)
        => new(session, [.. records.Select(r => r.Entity)], records,
            records.Count == 1 ? $"Delete {records[0].Entity.Name}" : $"Delete {records.Count} entities");

    public string Label => _label;

    public IReadOnlyCollection<WorldEntity> Entities => _entities;

    public bool ChangesMembership => true;

    public void Undo() => Toggle();

    public void Redo() => Toggle();

    public bool TryMerge(IEditStep next) => false;

    private void Toggle()
    {
        if (_records.Count == 0)
        {
            _records = [.. _entities.Select(_session.Delete)];
            return;
        }
        foreach (DeletedRecord record in _records.Reverse())
        {
            _session.Restore(record);
        }
        _records = [];
    }
}
