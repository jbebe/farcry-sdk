using System.Numerics;
using JackAll.App.MapEditor.Gl;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>Event links in the viewport: a line per link, and taking a click as a new link's target.</summary>
public partial class MapTabView
{
    private ShapeLayer? _linkLayer;

    /// <summary>Set by the inspector's "Link to…": the next entity clicked becomes the link's target.</summary>
    private bool _pickingLinkTarget;

    private void StartLinkPick()
    {
        if (_selection.Primary is null)
        {
            return;
        }
        _pickingLinkTarget = true;
        StatusText.Text = $"Click the entity {_selection.Primary.Name} should send to - Escape cancels";
        Viewport.Focus();
    }

    /// <summary>Takes a click as the pending link's target. Returns false when no pick is pending.</summary>
    private bool CompleteLinkPick(WorldEntity? hit)
    {
        if (!_pickingLinkTarget)
        {
            return false;
        }
        _pickingLinkTarget = false;
        StatusText.Text = hit is null ? "Link cancelled - nothing there"
            : _inspector.AddLink(hit) ? $"Linked to {hit.Name}"
            : "Pick an output and an event first";
        return true;
    }

    private void CancelLinkPick()
    {
        _pickingLinkTarget = false;
        StatusText.Text = "Link cancelled";
    }

    /// <summary>The link lines from the edited nodes. Needs the GL context.</summary>
    private void RebuildLinkLayer(WorldEditSession edits)
    {
        var lines = new List<WorldShape>();
        foreach (WorldEntity source in edits.World.Entities)
        {
            if (source.Position is not { } from)
            {
                continue;
            }
            foreach (EntityLink link in EntityLinks.Read(edits.CurrentNode(source)))
            {
                if (edits.EntityById(link.TargetId) is { Position: { } to })
                {
                    lines.Add(new WorldShape("link", link.EventName, source.Name, [from, to]));
                }
            }
        }
        _linkLayer?.Dispose();
        _linkLayer = new ShapeLayer(lines);
        LayerCatalog.Links.Status = $"{lines.Count:N0} links";
    }

    private WorldEntity? EntityById(ulong id) => _edits?.EntityById(id);
}
