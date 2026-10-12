using System.Windows.Controls;
using System.Windows.Input;
using JackAll.Tools.World;

namespace JackAll.App;

/// <summary>The undo and redo keys and buttons of every tab with an <see cref="EditHistory"/>.</summary>
internal static class UndoChrome
{
    /// <summary>Ctrl+Z undoes; Ctrl+Y and Ctrl+Shift+Z redo.</summary>
    public static bool IsUndoKey(Key key, out bool redo)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        redo = (key == Key.Y && modifiers == ModifierKeys.Control)
            || (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift));
        return redo || (key == Key.Z && modifiers == ModifierKeys.Control);
    }

    /// <summary>Enables the buttons by what the history holds, each naming the step it replays.</summary>
    public static void Refresh(Button undo, Button redo, EditHistory history)
    {
        undo.IsEnabled = history.CanUndo;
        redo.IsEnabled = history.CanRedo;
        undo.ToolTip = history.UndoLabel is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        redo.ToolTip = history.RedoLabel is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)";
    }
}
