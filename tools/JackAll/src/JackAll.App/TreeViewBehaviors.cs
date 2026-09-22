using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace JackAll.App;

/// <summary>Visual-tree walks shared by the app's TreeViews (the Files tab's folder tree and the FCB
/// editor's outline).</summary>
internal static class TreeViewBehaviors
{
    /// <summary>
    /// Expands or collapses an item on a single click anywhere on its row, like VS Code's explorer.
    /// Selection is left to the TreeView's own handling, which runs right after this. Wired from a
    /// TreeViewItem style's PreviewMouseLeftButtonDown EventSetter.
    /// </summary>
    public static void ToggleExpandOnItemClick(object sender, MouseButtonEventArgs e)
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

    /// <summary>
    /// The container for the last item of <paramref name="path"/>, realizing each level's containers on
    /// the way down, or null when one cannot be found. <paramref name="onEach"/> runs on every
    /// container reached, before the next level is looked up.
    /// </summary>
    public static TreeViewItem? RealizePath(ItemsControl root, IEnumerable<object> path, Action<TreeViewItem>? onEach = null)
    {
        ItemsControl parent = root;
        TreeViewItem? item = null;
        foreach (object step in path)
        {
            parent.UpdateLayout();
            if (ContainerFor(parent, step) is not TreeViewItem container)
            {
                return null;
            }
            item = container;
            onEach?.Invoke(item);
            parent = item;
        }
        return item;
    }

    /// <summary>The container for <paramref name="item"/>, scrolling a virtualizing panel to it first
    /// when it has none yet - a row far down a long list only gets one once it is near the view.</summary>
    private static TreeViewItem? ContainerFor(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem found)
        {
            return found;
        }

        int index = parent.Items.IndexOf(item);
        if (index < 0 || ItemsHost(parent) is not VirtualizingPanel panel)
        {
            return null;
        }
        panel.BringIndexIntoViewPublic(index);
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
    }

    /// <summary>The panel an items control lays its containers out in: the nearest one under its
    /// template's items presenter.</summary>
    private static VirtualizingPanel? ItemsHost(ItemsControl control)
    {
        control.ApplyTemplate();
        var pending = new Queue<DependencyObject>([control]);
        while (pending.TryDequeue(out DependencyObject? node))
        {
            if (node is ItemsPresenter presenter)
            {
                presenter.ApplyTemplate();
                return VisualTreeHelper.GetChildrenCount(presenter) > 0
                    ? VisualTreeHelper.GetChild(presenter, 0) as VirtualizingPanel
                    : null;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }
        return null;
    }

    /// <summary>The nearest ancestor of type <typeparamref name="T"/>, starting at (and including)
    /// <paramref name="node"/> itself.</summary>
    public static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        return node as T;
    }

    /// <summary>Walks up the visual tree from <paramref name="start"/> to the nearest ancestor whose
    /// own DataContext is a <typeparamref name="T"/> - how a control inside a per-item DataTemplate
    /// finds the view model of the ItemsControl it sits in.</summary>
    public static T? FindAncestorDataContext<T>(DependencyObject start) where T : class
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
