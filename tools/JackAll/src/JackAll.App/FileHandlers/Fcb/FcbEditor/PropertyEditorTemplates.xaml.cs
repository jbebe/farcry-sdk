using System.Windows;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>The value editors a <see cref="PropertyRow"/> shows, the field row around them, and the
/// buttons both carry. Merged into every view that edits fields.</summary>
public partial class PropertyEditorTemplates : ResourceDictionary
{
    public PropertyEditorTemplates()
    {
        InitializeComponent();
    }

    private void RevertField_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FieldView field)
        {
            field.Revert();
        }
    }

    private void RestoreField_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FieldView field)
        {
            field.Row.RestoreOriginal();
        }
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
