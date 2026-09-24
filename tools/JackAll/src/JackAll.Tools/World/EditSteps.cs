using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>Where an entity stood and faced.</summary>
public readonly record struct Placement(Vector3 Position, Vector3 Angles)
{
    public static Placement Of(WorldEntity entity) => new(entity.Position!.Value, entity.Angles);

    public void ApplyTo(WorldEntity entity)
    {
        entity.Position = Position;
        entity.Angles = Angles;
    }
}

/// <summary>Entities moved or turned, one drag or one typed transform.</summary>
/// <param name="mergeKey">Steps with the same non-null key on the same entity merge, so typing into a
/// transform field is one step; a gizmo drag passes null.</param>
public sealed class MoveStep(
    WorldEditSession session, IReadOnlyDictionary<WorldEntity, (Placement Before, Placement After)> moves,
    string? mergeKey = null) : IEditStep
{
    private readonly string? _mergeKey = mergeKey;
    private IReadOnlyDictionary<WorldEntity, (Placement Before, Placement After)> _moves = moves;

    /// <summary>One entity moved from <paramref name="before"/> to where it now stands.</summary>
    public static MoveStep Of(WorldEditSession session, WorldEntity entity, Placement before, string? mergeKey = null)
        => new(session, new Dictionary<WorldEntity, (Placement, Placement)> { [entity] = (before, Placement.Of(entity)) }, mergeKey);

    public string Label => _moves.Count == 1 ? $"Move {_moves.Keys.First().Name}" : $"Move {_moves.Count} entities";

    public IReadOnlyCollection<WorldEntity> Entities => [.. _moves.Keys];

    public bool ChangesMembership => false;

    public void Undo() => Apply(m => m.Before);

    public void Redo() => Apply(m => m.After);

    public bool TryMerge(IEditStep next)
    {
        if (_mergeKey is null || next is not MoveStep other || other._mergeKey != _mergeKey
            || _moves.Count != 1 || other._moves.Count != 1 || _moves.Keys.Single() != other._moves.Keys.Single())
        {
            return false;
        }

        WorldEntity entity = _moves.Keys.Single();
        _moves = new Dictionary<WorldEntity, (Placement, Placement)> { [entity] = (_moves[entity].Before, other._moves[entity].After) };
        return true;
    }

    private void Apply(Func<(Placement Before, Placement After), Placement> pick)
    {
        foreach ((WorldEntity entity, (Placement, Placement) move) in _moves)
        {
            pick(move).ApplyTo(entity);
            session.Moved(entity);
        }
    }
}

/// <summary>Entities refiled under another mission layer.</summary>
public sealed class LayerStep : IEditStep
{
    private readonly WorldEditSession _session;
    private readonly IReadOnlyDictionary<WorldEntity, string> _from;
    private readonly string _to;

    private LayerStep(WorldEditSession session, IReadOnlyDictionary<WorldEntity, string> from, string to)
    {
        _session = session;
        _from = from;
        _to = to;
    }

    /// <summary>Moves <paramref name="entities"/>, and any prefab's members with it, to
    /// <paramref name="layerPathId"/>, and returns the step that did it.</summary>
    public static LayerStep Move(WorldEditSession session, IEnumerable<WorldEntity> entities, string layerPathId)
    {
        var step = new LayerStep(session, session.WithMembers(entities).ToDictionary(e => e, e => e.LayerPathId), layerPathId);
        step.Redo();
        return step;
    }

    public string Label => _from.Count == 1 ? $"Move {_from.Keys.First().Name} to {_to}" : $"Move {_from.Count} entities to {_to}";

    public IReadOnlyCollection<WorldEntity> Entities => [.. _from.Keys];

    public bool ChangesMembership => true;

    public void Undo() => Apply(entity => _from[entity]);

    public void Redo() => Apply(_ => _to);

    public bool TryMerge(IEditStep next) => false;

    private void Apply(Func<WorldEntity, string> layerOf)
    {
        foreach (WorldEntity entity in _from.Keys)
        {
            _session.MoveToLayer(entity, layerOf(entity));
        }
    }
}

/// <summary>A change to an entity's node: a field, a component added or removed, a handle dragged.</summary>
public sealed class NodeEditStep(WorldEditSession session, WorldEntity entity, FcbObject before, FcbObject after, string label)
    : IEditStep
{
    private readonly WorldEntity _entity = entity;
    private FcbObject _after = after;

    /// <summary>The one value the edit changed, or null when it changed more or changed structure.</summary>
    private readonly string? _changed = SoleChange(before, after);

    public string Label => label;

    public IReadOnlyCollection<WorldEntity> Entities => [_entity];

    public bool ChangesMembership => false;

    public void Undo() => Apply(before);

    public void Redo() => Apply(_after);

    public bool TryMerge(IEditStep next)
    {
        if (next is not NodeEditStep other || other._entity != _entity || _changed is null || other._changed != _changed)
        {
            return false;
        }
        _after = other._after;
        return true;
    }

    private void Apply(FcbObject state)
    {
        session.EditableNode(_entity).Overwrite(state);
        session.Edited(_entity);
    }

    /// <summary>The path of the single value that differs between the two trees, or null.</summary>
    private static string? SoleChange(FcbObject a, FcbObject b)
    {
        string? changed = null;
        return Walk(a, b, "", ref changed) ? changed : null;
    }

    /// <summary>False once the trees differ in shape or in a second value.</summary>
    private static bool Walk(FcbObject a, FcbObject b, string path, ref string? changed)
    {
        if (a.TypeHash != b.TypeHash || a.Children.Count != b.Children.Count || a.Values.Count != b.Values.Count)
        {
            return false;
        }
        foreach ((uint hash, byte[] value) in a.Values)
        {
            if (!b.Values.TryGetValue(hash, out byte[]? other))
            {
                return false;
            }
            if (!value.AsSpan().SequenceEqual(other))
            {
                if (changed is not null)
                {
                    return false;
                }
                changed = $"{path}/{hash:X8}";
            }
        }
        for (int i = 0; i < a.Children.Count; i++)
        {
            if (!Walk(a.Children[i], b.Children[i], $"{path}/{i}", ref changed))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Entities added or deleted. Undoing one is doing the other, so this one step toggles.</summary>
public sealed class PresenceStep : IEditStep
{
    private readonly WorldEditSession _session;
    private readonly IReadOnlyList<WorldEntity> _entities;

    /// <summary>What restores the entities while they are gone; empty while they are present.</summary>
    private IReadOnlyList<DeletedRecord> _records;

    private PresenceStep(WorldEditSession session, IReadOnlyList<WorldEntity> entities, IReadOnlyList<DeletedRecord> records, string label)
    {
        _session = session;
        _entities = entities;
        _records = records;
        Label = label;
    }

    public static PresenceStep Added(WorldEditSession session, IReadOnlyList<WorldEntity> entities)
        => new(session, entities, [], entities.Count == 1 ? $"Add {entities[0].Name}" : $"Add {entities.Count} entities");

    public static PresenceStep Deleted(WorldEditSession session, IReadOnlyList<DeletedRecord> records)
        => new(session, [.. records.Select(r => r.Entity)], records,
            records.Count == 1 ? $"Delete {records[0].Entity.Name}" : $"Delete {records.Count} entities");

    public string Label { get; }

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
