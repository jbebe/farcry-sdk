using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>
/// One fragment-editor tab's view: an object tree on the left, a structured property grid on the
/// right - every field is a typed, validated control (see <see cref="PropertyRow"/>/
/// <see cref="ScalarField"/>), not free text. There is no XML in this view at all; XML only exists as
/// <see cref="FcbEditorTabViewModel"/>'s parse-once-at-open, render-once-at-save transport format.
/// </summary>
public partial class FcbEditorTabView : UserControl
{
    private readonly FcbEditorTabViewModel _vm;

    public FcbEditorTabView(FcbEditorTabViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Lets a host that focused an already-open tab still reposition it - see
    /// <c>MainWindow.OpenSectorEditorTab</c>.</summary>
    public FcbEditorTabViewModel ViewModel => _vm;

    private void OutlineTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _vm.SelectedNode = e.NewValue as FcbObjectNodeView;

    /// <summary>Single-click expand/collapse for the outline tree - see
    /// <see cref="TreeViewBehaviors.ToggleExpandOnItemClick"/>; the style's EventSetter needs an
    /// instance handler to bind to.</summary>
    private void OutlineTree_ItemClicked(object sender, MouseButtonEventArgs e)
        => TreeViewBehaviors.ToggleExpandOnItemClick(sender, e);

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
        if (button.Tag is ScalarField item && TreeViewBehaviors.FindAncestorDataContext<NumberArrayGroup>(button) is { } group)
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
        if (button.Tag is BoolField item && TreeViewBehaviors.FindAncestorDataContext<BoolArrayGroup>(button) is { } group)
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
        if (button.Tag is ScalarField item && TreeViewBehaviors.FindAncestorDataContext<VectorArrayGroup>(button) is { } group)
        {
            group.RemoveItem(item);
        }
    }
}
