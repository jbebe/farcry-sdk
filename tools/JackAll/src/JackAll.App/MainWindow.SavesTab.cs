using System.IO;
using System.Windows.Controls;
using System.Windows;

namespace JackAll.App;

/// <summary>The Saves tab's handlers: selection, opening a save in the FCB editor, and deletion.</summary>
public partial class MainWindow
{
    private void SavesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.SelectedSave = SavesGrid.SelectedItem as SaveRow;

    private void OpenSaveFcbEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSave is not { } save || _vm.SelectedSaveDetails?.DocumentXml is not { } xml) return;
        OpenSaveFcbEditorTab(save, xml);
    }

    private void DeleteSave_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSave is not { } save) return;

        if (MessageBox.Show(this,
                $"Permanently delete '{save.FileName}'?\n\nThis deletes the actual save file from disk - it cannot be undone.",
                "Delete save", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            File.Delete(save.Info.FilePath);
        }
        catch (Exception ex)
        {
            Warn($"Couldn't delete '{save.FileName}': {ex.Message}");
            return;
        }

        _vm.RemoveSaveRow(save);
    }
}
