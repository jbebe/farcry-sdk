using System.Windows;
using System.Windows.Input;
using JackAll.Core.Vfs;

namespace JackAll.App.Picker;

/// <summary>A modal browser over the merged archives that hands back one file.</summary>
public partial class FilePickerWindow : Window
{
    private readonly FilePickerViewModel _model;

    public FilePickerWindow(FilePickerViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) =>
        {
            RevealFolder();
            if (_model.SelectedFile is { } file)
            {
                FileGrid.ScrollIntoView(file);
            }
            SearchBox.Focus();
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilePickerViewModel.SelectedFolder) && FolderTree.SelectedItem != model.SelectedFolder)
            {
                Dispatcher.BeginInvoke(RevealFolder);
            }
        };
    }

    public VfsFile? Result => _model.Resolved;

    /// <summary>Opens the tree down to the selected folder and selects it there.</summary>
    private void RevealFolder()
    {
        if (_model.SelectedFolder is { } folder
            && TreeViewBehaviors.RealizePath(FolderTree, _model.AncestorChain(folder)) is { } item)
        {
            item.IsSelected = true;
            item.BringIntoView();
        }
    }

    private void Accept()
    {
        if (_model.CanAccept)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Accept();
        }
    }

    private void Row_DoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _model.SelectedFolder = e.NewValue as FolderNode;

    private void FolderTree_ItemClicked(object sender, MouseButtonEventArgs e)
        => TreeViewBehaviors.ToggleExpandOnItemClick(sender, e);
}
