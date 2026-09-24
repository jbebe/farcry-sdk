using System.Windows;
using System.Windows.Input;
using JackAll.App.Library;

namespace JackAll.App.MapEditor;

/// <summary>A modal search over the loaded world's archetypes that hands back one full name.</summary>
public partial class ArchetypePickerWindow : Window
{
    public ArchetypePickerWindow(ArchetypeTreeNode archetypes)
    {
        InitializeComponent();
        var model = new EntityLibraryViewModel();
        model.Load(archetypes);
        DataContext = model;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => SearchBox.Focus();
    }

    public string? Result => (Items.SelectedItem as ArchetypeTreeNode)?.FullName;

    private void Accept()
    {
        if (Result is not null)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Items_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => OkButton.IsEnabled = Result is not null;

    private void Items_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void Items_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Accept();
        }
    }

    /// <summary>Down moves from the search into the list, and Enter takes its first match.</summary>
    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Enter) || Items.Items.Count == 0)
        {
            return;
        }

        if (Items.SelectedIndex < 0)
        {
            Items.SelectedIndex = 0;
        }
        if (e.Key == Key.Enter)
        {
            Accept();
        }
        else
        {
            Items.UpdateLayout();
            (Items.ItemContainerGenerator.ContainerFromIndex(Items.SelectedIndex) as UIElement)?.Focus();
        }
        e.Handled = true;
    }
}
