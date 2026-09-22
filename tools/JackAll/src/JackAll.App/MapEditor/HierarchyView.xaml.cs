using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace JackAll.App.MapEditor;

/// <summary>The hierarchy panel. Gestures only; the state is <see cref="HierarchyViewModel"/>'s.</summary>
public partial class HierarchyView : UserControl
{
    public HierarchyView()
    {
        InitializeComponent();
    }

    private HierarchyViewModel Model => (HierarchyViewModel)DataContext;

    /// <summary>Raised when an archetype is dropped on a layer row, with the archetype and the layer.</summary>
    public event Action<string, string>? PlaceRequested;

    /// <summary>Raised for Del with the tree focused.</summary>
    public event Action? DeleteRequested;

    /// <summary>Scrolls the row for <paramref name="node"/> into view once its path is expanded.</summary>
    public void BringIntoView(EntityTreeNode node)
        => Dispatcher.BeginInvoke(
            () => TreeViewBehaviors.RealizePath(Tree, PathTo(node), item => item.BringIntoView()),
            System.Windows.Threading.DispatcherPriority.Background);

    private static IEnumerable<EntityTreeNode> PathTo(EntityTreeNode node)
    {
        var path = new Stack<EntityTreeNode>();
        for (EntityTreeNode? step = node; step is not null; step = step.Parent)
        {
            path.Push(step);
        }
        return path;
    }

    /// <summary>A preview event tunnels through every ancestor row, so only the innermost acts; a
    /// click on the chevron or a toggle is left to that control.</summary>
    private void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (sender is not TreeViewItem { DataContext: EntityTreeNode node } item
            || TreeViewBehaviors.Ancestor<TreeViewItem>(source) != item
            || TreeViewBehaviors.Ancestor<ButtonBase>(source) is not null)
        {
            return;
        }

        Model.Click(node, Keyboard.Modifiers);
        Tree.Focus();
        e.Handled = true;
        if (node is { IsEntity: true, Entity: { } entity })
        {
            EntityClicked?.Invoke(entity);
        }
    }

    /// <summary>Raised after a click on an entity's own row, with that entity.</summary>
    public event Action<Tools.World.WorldEntity>? EntityClicked;

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EntityTreeNode node })
        {
            Model.ToggleHidden(node);
        }
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EntityTreeNode node })
        {
            Model.ToggleLocked(node);
        }
    }

    private void LayerTick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EntityTreeNode node })
        {
            Model.LayerToggled(node);
        }
    }

    private void AllLayers_Click(object sender, RoutedEventArgs e) => Model.SetAllLayers(_ => true);

    private void OnlyMain_Click(object sender, RoutedEventArgs e)
        => Model.SetAllLayers(Core.Format.Fcb.MissionLayers.IsMain);

    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is TreeViewItem { DataContext: EntityTreeNode { IsLayer: true } }
            && e.Data.GetDataPresent(EntityLibraryViewModel.DragFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void Row_Drop(object sender, DragEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: EntityTreeNode { IsLayer: true } layer }
            && e.Data.GetData(EntityLibraryViewModel.DragFormat) is string archetype)
        {
            PlaceRequested?.Invoke(archetype, layer.LayerPathId!);
            e.Handled = true;
        }
    }
}
