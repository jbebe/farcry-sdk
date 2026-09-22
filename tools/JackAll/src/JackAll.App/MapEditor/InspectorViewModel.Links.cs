using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>One of the inspected entity's event links, as the Links section lists it.</summary>
public sealed record LinkRow(int Index, string Text);

/// <summary>An event a link can send, as the add form offers it.</summary>
public sealed record LinkEvent(string Class, string Name)
{
    public override string ToString() => $"{Name}  ({Class})";
}

/// <summary>The inspector's Links section: the entity's event links, and adding one by picking its
/// target in the viewport.</summary>
public sealed partial class InspectorViewModel
{
    private IReadOnlyList<LinkRow> _links = [];
    private string _newLinkOutput = "";
    private LinkEvent? _newLinkEvent;

    /// <summary>Resolves a link's target id to the entity it names; set once a world loads.</summary>
    public Func<ulong, WorldEntity?>? EntityById { get; set; }

    public IReadOnlyList<LinkRow> Links
    {
        get => _links;
        private set => Set(ref _links, value);
    }

    /// <summary>The outputs and events the loaded world's own links use, which is what the form offers.</summary>
    public IReadOnlyList<string> LinkOutputs { get; private set; } = [];

    public IReadOnlyList<LinkEvent> LinkEvents { get; private set; } = [];

    public string NewLinkOutput
    {
        get => _newLinkOutput;
        set => Set(ref _newLinkOutput, value);
    }

    public LinkEvent? NewLinkEvent
    {
        get => _newLinkEvent;
        set => Set(ref _newLinkEvent, value);
    }

    /// <summary>Gathers what the form offers from every link <paramref name="entities"/> carry.</summary>
    public void LoadLinkCatalog(IEnumerable<WorldEntity> entities)
    {
        List<EntityLink> all = [.. entities.SelectMany(e => EntityLinks.Read(e.Node))];
        LinkOutputs = [.. all.Select(l => l.Output).Where(o => o.Length > 0).Distinct().Order()];
        LinkEvents = [.. all.Select(l => new LinkEvent(l.EventClass, l.EventName)).Where(e => e.Name.Length > 0).Distinct()
            .OrderBy(e => e.Name, StringComparer.Ordinal)];
        OnPropertyChanged(nameof(LinkOutputs));
        OnPropertyChanged(nameof(LinkEvents));
        NewLinkOutput = LinkOutputs.FirstOrDefault() ?? "";
        NewLinkEvent = LinkEvents.FirstOrDefault();
    }

    /// <summary>Links the inspected entity to <paramref name="target"/> with the form's output and event.</summary>
    public bool AddLink(WorldEntity target)
    {
        if (_entity is not { } entity || Session is not { } session || NewLinkEvent is not { } ev || NewLinkOutput.Length == 0)
        {
            return false;
        }
        EntityLinks.Add(session.EditableNode(entity), new EntityLink(NewLinkOutput, target.Id, ev.Class, ev.Name));
        Commit(entity, $"Link {entity.Name} to {target.Name}");
        Reload();
        return true;
    }

    public void RemoveLink(LinkRow row)
    {
        if (_entity is not { } entity || Session is not { } session)
        {
            return;
        }
        EntityLinks.RemoveAt(session.EditableNode(entity), row.Index);
        Commit(entity, $"Unlink {entity.Name}");
        Reload();
    }

    private void RefreshLinks()
    {
        Links = _entity is { } entity && Session is { } session
            ? [.. EntityLinks.Read(session.CurrentNode(entity)).Select((link, i) => new LinkRow(i,
                $"{link.Output} → {link.EventName} on {EntityById?.Invoke(link.TargetId)?.Name ?? $"missing {link.TargetId}"}"))]
            : [];
    }
}
