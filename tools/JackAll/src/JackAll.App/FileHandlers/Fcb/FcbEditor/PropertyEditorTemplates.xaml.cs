using System.Windows;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>The value editors a <see cref="PropertyRow"/> shows, and the array buttons they carry.
/// Merged into every view that hosts a property grid.</summary>
public partial class PropertyEditorTemplates : ResourceDictionary
{
    public PropertyEditorTemplates()
    {
        InitializeComponent();
    }

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
