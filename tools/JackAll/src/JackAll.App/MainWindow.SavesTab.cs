using JackAll.Core.Format.Fcb;
using JackAll.Tools.Sav;
using System.IO;
using System.Windows.Controls;
using System.Windows;

namespace JackAll.App;

/// <summary>The Saves tab's handlers: selection, opening a save in the FCB editor, purging its
/// persisted entities into a new save, and deletion.</summary>
public partial class MainWindow
{
    private void SavesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.SelectedSave = SavesGrid.SelectedItem as SaveRow;

    private void OpenSaveFcbEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSave is not { } save || _vm.SelectedSaveDetails?.DocumentXml is not { } xml) return;
        OpenSaveFcbEditorTab(save, xml);
    }

    private async void PurgeSave_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSave is not { } save) return;

        if (MessageBox.Show(this,
                $"Write a copy of '{save.FileName}' with its persisted entities dropped?\n\n" +
                "In the copy, every entity respawns from the game's current entity library, so a mod " +
                "installed after this save was made takes effect. The world resets with it: cleared " +
                "outposts repopulate, destroyed props return, and items dropped on the ground are gone. " +
                "Mission progress, buddies, tapes and diamonds carry over.\n\n" +
                $"'{save.FileName}' itself is not modified.",
                "Purge persisted entities", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        string destPath;
        PurgeReport report;
        try
        {
            (destPath, report) = await Task.Run(() => SaveGameCleaner.PurgeToNewSave(save.Info));
        }
        catch (Exception ex)
        {
            Warn($"Couldn't write the cleaned copy: {ex.Message}");
            return;
        }

        _vm.AddSaveRow(destPath);
        _vm.Status = $"Wrote {Path.GetFileName(destPath)} - dropped {report.RecordsRemoved:N0} persisted record(s).";
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
