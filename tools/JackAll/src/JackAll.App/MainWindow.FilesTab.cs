using JackAll.Core.Vfs;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;

namespace JackAll.App;

/// <summary>The Files tab's handlers: folder-tree and grid selection, single/bulk export, and the
/// per-file workspace actions (replace, mirror, revert, copy).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Reveal-only tree selections (see <see cref="RevealSelectedFileInTree"/>) are just visual
    /// context, not the user asking to browse a different folder — letting them through here would
    /// rebuild the file list mid-search and drop whatever the grid had selected.
    /// </summary>
    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_revealingTreeSelection) return;
        _vm.SelectedFolder = e.NewValue as FolderNode;
    }

    private void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.SetSelectedFiles(FileGrid.SelectedItems.Cast<VfsFile>().ToList());

    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFiles.Count == 0) return;

        var dialog = new OpenFolderDialog { Title = "Export files to…" };
        if (dialog.ShowDialog(this) != true) return;

        int exported = 0;
        foreach (VfsFile file in _vm.SelectedFiles)
        {
            try
            {
                File.WriteAllBytes(Path.Combine(dialog.FolderName, file.FileName), _vm.Read(file));
                exported++;
            }
            catch (Exception ex)
            {
                Warn($"Couldn't export '{file.FileName}': {ex.Message}");
            }
        }

        _vm.Status = $"Exported {exported} of {_vm.SelectedFiles.Count} file(s) to {dialog.FolderName}.";
    }

    /// <summary>
    /// Writes the whole subtree under the selected folder to disk, folder structure and all — the bulk
    /// counterpart of <see cref="Export_Click"/>, reached from the details pane or the tree's own
    /// right-click menu (both act on <see cref="MainViewModel.SelectedFolder"/>; right-clicking a row
    /// selects it first, see <see cref="FolderTree_ItemRightClicked"/>).
    /// </summary>
    private async void ExportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFolder is not { } folder)
        {
            Warn("Pick a folder in the tree first.");
            return;
        }

        if (_vm.IsExporting)
        {
            Warn("A folder export is already running. Wait for it to finish, or cancel it from the status bar.");
            return;
        }

        IReadOnlyList<VfsFile> files = _vm.FilesUnder(folder);
        if (files.Count == 0)
        {
            Warn($"There's nothing to export under '{folder.FullPath}'.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = $"Export {folder.FullPath} to…" };
        if (dialog.ShowDialog(this) != true) return;

        // A subtree near the top of the tree is tens of thousands of files and gigabytes of data, and
        // the destination is somewhere the user picked - worth stating both before writing into it.
        string destination = dialog.FolderName;
        long totalBytes = files.Sum(f => f.Size);
        if (MessageBox.Show(this,
                $"Export {files.Count:N0} file(s) ({MainViewModel.FormatSize(totalBytes)}) from " +
                $"'{folder.FullPath}' into:\n\n{destination}\n\n" +
                "The folder structure is recreated there, and files already at those paths are overwritten.",
                "Export folder", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        _vm.Busy = true;
        _vm.Status = $"Exporting {files.Count:N0} file(s) from {folder.FullPath}…";
        try
        {
            FolderExportResult result = await _vm.ExportFolderAsync(folder, files, destination);

            string skipped = result.Failed > 0 ? $" {result.Failed:N0} couldn't be read and were skipped." : string.Empty;
            _vm.Status = result.Cancelled
                ? $"Export cancelled - {result.Written:N0} of {files.Count:N0} file(s) had already been " +
                  $"written to {destination}.{skipped}"
                : $"Exported {result.Written:N0} file(s) from {folder.FullPath} to {destination}.{skipped}";

            if (result.FirstError is { } error)
            {
                Warn($"{result.Failed:N0} file(s) couldn't be exported. The first one was:\n\n{error}");
            }
        }
        catch (Exception ex)
        {
            _vm.Status = "Export failed.";
            Warn($"Couldn't export '{folder.FullPath}': {ex.Message}");
        }
        finally
        {
            _vm.Busy = false;
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        _vm.CancelExport();
        _vm.Status = "Stopping the export…";
    }

    /// <summary>Single-click expand/collapse for the folder tree - see
    /// <see cref="TreeViewBehaviors.ToggleExpandOnItemClick"/>; the style's EventSetter needs an
    /// instance handler to bind to.</summary>
    private void FolderTree_ItemClicked(object sender, MouseButtonEventArgs e)
        => TreeViewBehaviors.ToggleExpandOnItemClick(sender, e);

    /// <summary>Selects the row under the cursor so the context menu that's about to open acts on it
    /// (see the style's ContextMenu in MainWindow.xaml) — a TreeView selects on left-click only.
    /// Same innermost-item guard as <see cref="TreeViewBehaviors.ToggleExpandOnItemClick"/>, for the
    /// same reason.</summary>
    private void FolderTree_ItemRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item) return;
        if (TreeViewBehaviors.Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) != item) return;

        item.IsSelected = true;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file)
        {
            Warn("Pick a file first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export file",
            FileName = file.FileName,
            Filter = $"{file.Type.Extension} file|*.{file.Type.Extension}|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _vm.Read(file));
            _vm.Status = $"Exported {file.FileName}.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't export that file: {ex.Message}");
        }
    }

    /// <summary>Exports the base game's own bytes for the selected file, ignoring whatever mod/workspace
    /// edit currently wins - only enabled (see <see cref="MainViewModel.HasOriginal"/>) when there is
    /// one to export.</summary>
    private void ExportOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file)
        {
            Warn("Pick a file first.");
            return;
        }

        byte[]? original = _vm.ReadOriginal(file);
        if (original is null)
        {
            Warn($"'{file.FileName}' has no base game version to export - it was added entirely by a mod.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export original file",
            FileName = file.FileName,
            Filter = $"{file.Type.Extension} file|*.{file.Type.Extension}|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, original);
            _vm.Status = $"Exported the base game version of {file.FileName}.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't export that file: {ex.Message}");
        }
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file)
        {
            Warn("Pick a file first.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Replace {file.FileName}",
            Filter = $"{file.Type.Extension} file|*.{file.Type.Extension}|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _vm.Replace(file, File.ReadAllBytes(dialog.FileName));
            _vm.Status = $"{file.FileName} staged in your workspace. Press Deploy all mods to put it into the game.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't stage that file: {ex.Message}");
        }
    }

    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file)
        {
            Warn("Pick a file first.");
            return;
        }

        if (!ConfirmWorkspaceOverwrite(file)) return;

        try
        {
            _vm.Replace(file, _vm.Read(file));
            _vm.Status = $"{file.FileName} mirrored into your workspace. Press Deploy all mods to put it into the game.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't mirror '{file.FileName}': {ex.Message}");
        }
    }

    private void MirrorOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file)
        {
            Warn("Pick a file first.");
            return;
        }

        byte[]? original = _vm.ReadOriginal(file);
        if (original is null)
        {
            Warn($"'{file.FileName}' has no base game version to mirror - it was added entirely by a mod.");
            return;
        }

        if (!ConfirmWorkspaceOverwrite(file)) return;

        try
        {
            _vm.Replace(file, original);
            _vm.Status = $"{file.FileName} (original) mirrored into your workspace. Press Deploy all mods to put it into the game.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't mirror '{file.FileName}': {ex.Message}");
        }
    }

    /// <summary>Shared by Mirror_Click/MirrorOriginal_Click: true to proceed (nothing staged there
    /// yet, or the user confirmed overwriting what already is), false to back out untouched.</summary>
    private bool ConfirmWorkspaceOverwrite(VfsFile file)
    {
        if (!_vm.IsStagedInWorkspace(file)) return true;

        return MessageBox.Show(this,
            $"'{file.FileName}' is already staged in your workspace. Overwrite it?",
            "Mirror to workspace", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file) return;

        if (!file.IsModded)
        {
            Warn("This file isn't modded, so there's nothing to revert.");
            return;
        }

        // This row isn't itself staged anywhere - it's just that one or more of its fragments are
        // (see VfsFile.FragmentOverrideSource). There's nothing here to unstage; the fix is to revert
        // (or disable the mod behind) each overridden fragment individually.
        if (file.FragmentOverrideSource is { } source)
        {
            Warn($"'{file.FileName}' isn't itself replaced - {source} overrides one or more fragments " +
                 "inside it.\n\nOpen it as a folder in the tree and revert (or disable the mod behind) " +
                 "each overridden fragment there.");
            return;
        }

        if (_vm.Revert(file))
        {
            _vm.Status = $"{file.FileName} is back to the game's original. Deploy all mods to make it so in-game.";
        }
        else
        {
            // It comes from a mod zip, not the workspace. The tool won't reach into someone else's
            // mod and delete from it — switching the mod off is the honest way to undo that.
            Warn($"'{file.FileName}' comes from the mod '{file.SourceName}', not from your own edits.\n\n" +
                 "Switch that mod off on the Mods tab to stop it applying.");
        }
    }

    private void CopyHash_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file) return;
        Clipboard.SetText($"{file.Hash:X8}");
        _vm.Status = $"Copied {file.Hash:X8}.";
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is not { } file) return;
        Clipboard.SetText(file.Path);
        _vm.Status = $"Copied {file.Path}.";
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "JackAll", MessageBoxButton.OK, MessageBoxImage.Information);
}
