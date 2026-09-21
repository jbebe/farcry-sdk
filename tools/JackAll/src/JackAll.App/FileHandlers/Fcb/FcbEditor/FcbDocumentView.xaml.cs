using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>An <see cref="FcbDocumentViewModel"/>'s outline and the selected row's view. Gestures only.</summary>
public partial class FcbDocumentView : UserControl
{
    public FcbDocumentView(FcbDocumentViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = vm;
    }

    public FcbDocumentViewModel ViewModel { get; }

    private void OutlineTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => ViewModel.SelectedNode = e.NewValue as OutlineNode;

    private void OutlineTree_ItemClicked(object sender, MouseButtonEventArgs e)
        => TreeViewBehaviors.ToggleExpandOnItemClick(sender, e);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string? error = await ViewModel.SaveAsync();
        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "JackAll", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
