using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackAll.App.Library;

namespace JackAll.App.MapEditor;

/// <summary>The entity library panel. Gestures only; the state is <see cref="EntityLibraryViewModel"/>'s.</summary>
public partial class EntityLibraryView : UserControl
{
    private Point? _pressedAt;

    public EntityLibraryView()
    {
        InitializeComponent();
    }

    private void Folders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => ((EntityLibraryViewModel)DataContext).SelectedFolder = e.NewValue as ArchetypeTreeNode;

    private void Items_MouseDown(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition(Items);

    private void Items_MouseMove(object sender, MouseEventArgs e)
        => DragFrom(Items, e, row => row is ArchetypeTreeNode { FullName: { } name }
            ? new DataObject(EntityLibraryViewModel.DragFormat, name)
            : null);

    private void Prefabs_MouseDown(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition(PrefabList);

    private void Prefabs_MouseMove(object sender, MouseEventArgs e)
        => DragFrom(PrefabList, e, row => row is SavedPrefab prefab
            ? new DataObject(EntityLibraryViewModel.PrefabDragFormat, prefab.Path)
            : null);

    /// <summary>Starts the drag once the pointer has moved far enough to mean it; starting it on the
    /// press itself would swallow the click that selects the row.</summary>
    private void DragFrom(ListBox list, MouseEventArgs e, Func<object?, DataObject?> payload)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedAt is not { } pressed)
        {
            return;
        }

        Vector moved = e.GetPosition(list) - pressed;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pressedAt = null;
        if (payload(TreeViewBehaviors.Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext) is { } data)
        {
            DragDrop.DoDragDrop(list, data, DragDropEffects.Copy);
        }
    }
}
