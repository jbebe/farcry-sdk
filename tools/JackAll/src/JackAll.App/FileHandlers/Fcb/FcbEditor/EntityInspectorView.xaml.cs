using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>An <see cref="EntityInspector"/>'s component foldouts. Gestures only.</summary>
public partial class EntityInspectorView : UserControl
{
    public EntityInspectorView()
    {
        InitializeComponent();
    }

    private void RemoveComponent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NodeView section })
        {
            section.Remove();
        }
    }

    private void AddComponent_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntityInspector inspector && ComponentToAdd.Text is { Length: > 0 } name
            && inspector.AddableComponents.Contains(name))
        {
            inspector.AddComponent(name);
            ComponentToAdd.Text = "";
        }
    }
}
