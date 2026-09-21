using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.MapEditor;

/// <summary>The inspector panel. Gestures only; the state is <see cref="InspectorViewModel"/>'s.</summary>
public partial class InspectorView : UserControl
{
    public InspectorView()
    {
        InitializeComponent();
    }

    public event Action? ShowArchetypeRequested;
    public event Action? OpenSectorRequested;
    public event Action? CopyRequested;
    public event Action? DeleteRequested;

    private void ShowArchetype_Click(object sender, RoutedEventArgs e) => ShowArchetypeRequested?.Invoke();

    private void OpenSector_Click(object sender, RoutedEventArgs e) => OpenSectorRequested?.Invoke();

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
}
