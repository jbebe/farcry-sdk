using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>The viewport's undo and redo over the loaded world's edits.</summary>
public partial class MapTabView
{
    /// <summary>Replaced whenever a world loads, which is what clears it.</summary>
    private EditHistory _history = new();

    private void StartHistory()
    {
        _history = new EditHistory();
        _history.Changed += RefreshHistoryButtons;
        _inspector.History = _history;
        RefreshHistoryButtons();
    }

    private void RefreshHistoryButtons() => UndoChrome.Refresh(UndoButton, RedoButton, _history);

    private void Undo_Click(object sender, System.Windows.RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, System.Windows.RoutedEventArgs e) => Redo();

    private void Undo() => Replayed(IsDragging ? null : _history.Undo(), "Undid");

    private void Redo() => Replayed(IsDragging ? null : _history.Redo(), "Redid");

    /// <summary>Brings every view back in line with the world after a step was undone or redone.</summary>
    private void Replayed(IEditStep? replayed, string verb)
    {
        if (replayed is not IWorldEditStep step || _edits is null)
        {
            return;
        }

        if (step.ChangesMembership)
        {
            _positionedEntities = [.. _edits.World.Entities.Where(e => e.Position is not null)];
            HashSet<WorldEntity> present = [.. _edits.World.Entities];
            _selection.Replace(_selection.Items.Where(present.Contains).ToList());
            EntitySetChanged();
        }
        else
        {
            _hierarchy.RefreshModified(step.Entities);
            RefreshSaveButton();
        }

        _markersDirty = true;
        _inspector.Reload();
        StatusText.Text = $"{verb} {step.Label}";
        OverlaysChanged();
    }
}
