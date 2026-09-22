using System.Windows.Input;
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

    private void RefreshHistoryButtons()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        UndoButton.ToolTip = _history.UndoLabel is { } undo ? $"Undo {undo} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        RedoButton.ToolTip = _history.RedoLabel is { } redo ? $"Redo {redo} (Ctrl+Y)" : "Redo (Ctrl+Y)";
    }

    /// <summary>Ctrl+Z undoes; Ctrl+Y and Ctrl+Shift+Z redo.</summary>
    private static bool IsUndoKey(Key key, out bool redo)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        redo = (key == Key.Y && modifiers == ModifierKeys.Control)
            || (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift));
        return redo || (key == Key.Z && modifiers == ModifierKeys.Control);
    }

    private void Undo_Click(object sender, System.Windows.RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, System.Windows.RoutedEventArgs e) => Redo();

    private void Undo() => Replayed(IsDragging ? null : _history.Undo(), "Undid");

    private void Redo() => Replayed(IsDragging ? null : _history.Redo(), "Redid");

    /// <summary>Brings every view back in line with the world after a step was undone or redone.</summary>
    private void Replayed(IEditStep? step, string verb)
    {
        if (step is null || _edits is null)
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
