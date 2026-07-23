using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace JackAll.App.XmlEditor;

/// <summary>
/// One fragment-editor tab's view: an object tree on the left, a structured property grid on the
/// right - every field is a typed, validated control (see <see cref="PropertyRow"/>/
/// <see cref="ScalarField"/>), not free text. There is no XML in this view at all; XML only exists as
/// <see cref="XmlEditorTabViewModel"/>'s parse-once-at-open, render-once-at-save transport format.
/// </summary>
public partial class XmlEditorTabView : UserControl
{
    private readonly XmlEditorTabViewModel _vm;

    public XmlEditorTabView(XmlEditorTabViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OutlineTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _vm.SelectedNode = e.NewValue as FcbObjectNodeView;

    /// <summary>
    /// Expands or collapses a node on a single click anywhere on its row, instead of requiring a
    /// double-click. Selection is left to the TreeView's own handling, which runs right after this.
    /// </summary>
    private void OutlineTree_ItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item || !item.HasItems)
        {
            return;
        }

        // A preview event tunnels through every ancestor item on the way down, so each one of them
        // sees this click. Only the innermost — the row actually under the cursor — should act.
        if (Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) != item)
        {
            return;
        }

        // The chevron already toggles itself; toggling again here would cancel it out.
        if (Ancestor<ToggleButton>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        item.IsExpanded = !item.IsExpanded;
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        return node as T;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string? error = await _vm.SaveAsync();
        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "JackAll", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestoreOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PropertyRow row)
        {
            row.RestoreOriginal();
        }
    }

    // ---------------------------------------------------------------- array add/remove

    private void AddNumberArrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NumberArrayGroup group)
        {
            group.AddItem();
        }
    }

    private void RemoveNumberArrayItem_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.Tag is ScalarField item && FindAncestorDataContext<NumberArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }

    private void AddBoolArrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BoolArrayGroup group)
        {
            group.AddItem();
        }
    }

    private void RemoveBoolArrayItem_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.Tag is BoolField item && FindAncestorDataContext<BoolArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }

    private void AddVectorArrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is VectorArrayGroup group)
        {
            group.AddItem();
        }
    }

    private void RemoveVectorArrayItem_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.Tag is ScalarField item && FindAncestorDataContext<VectorArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }

    /// <summary>Walks up the visual tree from <paramref name="start"/> to the nearest ancestor whose
    /// own DataContext is a <typeparamref name="T"/> - a remove button's Tag carries the item to
    /// remove, and this is how it finds the group that item belongs to (the ItemsControl one level up,
    /// whose DataContext is the group itself, never overridden by the per-item DataTemplate).</summary>
    private static T? FindAncestorDataContext<T>(DependencyObject start) where T : class
    {
        for (DependencyObject? node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: T match })
            {
                return match;
            }
        }
        return null;
    }
}
