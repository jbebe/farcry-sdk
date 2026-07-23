using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JackAll.App.FileHandlers;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.SaveGames;
using JackAll.App.XmlEditor;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Sav;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;
using Microsoft.Win32;

namespace JackAll.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
        Closing += (_, _) => _vm.SaveConfig();
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedFile))
        {
            RefreshPreview();
            RevealSelectedFileInTree();
        }
    }

    /// <summary>
    /// True while <see cref="RevealSelectedFileInTree"/> is programmatically selecting a
    /// <see cref="TreeViewItem"/> purely to show context - tells <see cref="FolderTree_SelectedItemChanged"/>
    /// not to treat that as the user browsing to a new folder (see remarks on that method for why).
    /// </summary>
    private bool _revealingTreeSelection;

    /// <summary>
    /// Expands and selects the tree node for the selected file's folder, without disturbing the file
    /// grid's own selection or keyboard focus. Mostly matters while the text filter is active — that
    /// shows matches from every folder, so the folder actually holding whichever one you clicked is
    /// often not the one already open in the tree.
    /// </summary>
    private void RevealSelectedFileInTree()
    {
        // Only the filtered, cross-folder view can point at a file outside the folder already open
        // in the tree - plain browsing never needs a jump, so skip it rather than rely on the target-
        // equals-current-folder check below to happen to no-op.
        if (string.IsNullOrWhiteSpace(_vm.FilterText)) return;

        if (_vm.SelectedFile is not { } file) return;

        FolderNode? target = _vm.FindFolder(file.Directory);
        if (target is null || target == _vm.SelectedFolder) return;

        ItemsControl parent = FolderTree;
        TreeViewItem? item = null;
        foreach (FolderNode node in _vm.GetAncestorChain(target))
        {
            parent.UpdateLayout(); // realizes containers for the level we're about to look up
            if (parent.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem container)
            {
                return; // virtualized out of existence, or the tree changed underneath us - bail quietly
            }

            item = container;
            item.IsExpanded = true;
            parent = item;
        }

        if (item is null) return;

        // Selecting a TreeViewItem also moves keyboard focus to it, which would otherwise pull focus
        // (and, worse, the DataGrid's own selection) away from the file just clicked — restore both
        // once the selection settles.
        IInputElement? previousFocus = Keyboard.FocusedElement;
        _revealingTreeSelection = true;
        try
        {
            item.IsSelected = true;
            item.BringIntoView();
        }
        finally
        {
            _revealingTreeSelection = false;
        }
        previousFocus?.Focus();
    }

    /// <summary>Asks FileHandlerCatalog for the view that matches the selected file's type, if any.</summary>
    private void RefreshPreview()
    {
        VfsFile? file = _vm.SelectedFile;
        UserControl? view = file is not null
            ? FileHandlerCatalog.CreateView(
                file, () => _vm.Read(file), bytes => _vm.Replace(file, bytes), () => OpenXmlEditorTab(file),
                () => _vm.ReadOriginal(file))
            : null;

        PreviewHost.Content = view;
        PreviewHost.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
        NoPreviewPanel.Visibility = file is not null && view is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ fragment XML editor tabs

    /// <summary>Open editor tabs, keyed by the fragment's own hash - lets "Open in XML Editor…" just
    /// focus an already-open tab instead of opening a second copy of the same content.</summary>
    private readonly Dictionary<uint, (TabItem Tab, XmlEditorTabViewModel ViewModel)> _openEditors = [];

    private void OpenXmlEditorTab(VfsFile file)
    {
        if (_openEditors.TryGetValue(file.Hash, out var existing))
        {
            MainTabs.SelectedItem = existing.Tab;
            return;
        }

        string xml;
        string? originalXml;
        try
        {
            xml = new UTF8Encoding(false).GetString(_vm.Read(file)).TrimStart((char)0xFEFF);
            // Null for a mod-added fragment (no archive provides its container) - nothing to diff
            // against, so every value in it just reads as unremarkable base content.
            originalXml = _vm.ReadOriginalFragment(file);
        }
        catch (Exception ex)
        {
            Warn($"Couldn't open '{file.FileName}': {ex.Message}");
            return;
        }

        var vm = new XmlEditorTabViewModel(
            file.FileName, file.Hash, xml, originalXml, FcbDefinitionsProvider.Value.Value,
            persist: async root =>
            {
                string rendered = await Task.Run(() => FcbXml.RenderObject(root, FcbDefinitionsProvider.Value.Value));
                _vm.Replace(file, new UTF8Encoding(false).GetBytes(rendered));
                return null;
            });
        var view = new XmlEditorTabView(vm);
        var tab = new TabItem { Content = view };
        tab.Header = BuildClosableTabHeader(tab, vm, () => _openEditors.Remove(file.Hash));

        _openEditors[file.Hash] = (tab, vm);
        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
    }

    /// <summary>Open save-tree editor tabs, keyed by the save's own file path - same
    /// dedup-by-focusing-the-existing-tab behavior as <see cref="_openEditors"/>, just keyed by path
    /// since a save has no <c>VfsFile.Hash</c> of its own.</summary>
    private readonly Dictionary<string, (TabItem Tab, XmlEditorTabViewModel ViewModel)> _openSaveEditors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Saves tab's "Open in XML Editor…" launcher - same tree/property-grid view the Files
    /// tab's fragments get, and just as editable: Save here writes straight back into <paramref name="save"/>'s
    /// own `.sav` file via <see cref="SaveGameDocument.WriteFcbRoot"/>, in place, no confirmation and no
    /// backup - unlike a mod fragment there's no workspace/deploy step in between, so this really is the
    /// player's real save the moment Save is clicked. <paramref name="documentXml"/> is only ever parsed
    /// to build the tree; what actually gets written back is <c>root</c>, the tree <see cref="XmlEditorTabViewModel"/>
    /// mutates in place as rows are edited - never <paramref name="documentXml"/> itself again.</summary>
    private void OpenSaveXmlEditorTab(SaveRow save, string documentXml)
    {
        string key = save.Info.FilePath;
        if (_openSaveEditors.TryGetValue(key, out var existing))
        {
            MainTabs.SelectedItem = existing.Tab;
            return;
        }

        var vm = new XmlEditorTabViewModel(
            save.FileName, hash: 0, documentXml, vanillaXml: null, FcbDefinitionsProvider.Value.Value,
            persist: async root =>
            {
                try
                {
                    await Task.Run(() => SaveGameDocument.WriteFcbRoot(save.Info, root));
                    _vm.RefreshSaveRow(save.Info.FilePath);
                    return null;
                }
                catch (Exception ex)
                {
                    return $"Couldn't write '{save.FileName}' back to disk: {ex.Message}";
                }
            },
            useSaveGameNameHarvest: true);
        var view = new XmlEditorTabView(vm);
        var tab = new TabItem { Content = view };
        tab.Header = BuildClosableTabHeader(tab, vm, () => _openSaveEditors.Remove(key));

        _openSaveEditors[key] = (tab, vm);
        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
    }

    /// <summary>Title plus a small "×" close button, since the two static tabs (Mods/Files) are the
    /// only ones that don't need one - matches the plain-code-behind tab management above rather than
    /// pulling in a DataTemplate/ItemsSource restructuring for a TabControl that otherwise stays as
    /// declared in XAML.</summary>
    private FrameworkElement BuildClosableTabHeader(TabItem tab, XmlEditorTabViewModel vm, Action onRemoved)
    {
        var text = new TextBlock { Text = vm.Title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(XmlEditorTabViewModel.IsDirty))
            {
                text.Text = vm.IsDirty ? $"{vm.Title} *" : vm.Title;
            }
        };

        var close = new Button
        {
            Content = "×",
            Padding = new Thickness(4, 0, 4, 0),
            MinWidth = 0,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            ToolTip = "Close",
        };
        close.Click += async (_, _) => await CloseEditorTabAsync(tab, vm, onRemoved);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(text);
        panel.Children.Add(close);
        return panel;
    }

    /// <summary>Prompts for unsaved changes before closing - Save runs the exact same
    /// <see cref="XmlEditorTabViewModel.SaveAsync"/> path as the tab's own Save button. A failed save
    /// leaves the tab open rather than closing anyway, so a bad edit is never silently discarded.</summary>
    private async Task CloseEditorTabAsync(TabItem tab, XmlEditorTabViewModel vm, Action onRemoved)
    {
        if (vm.IsDirty)
        {
            MessageBoxResult choice = MessageBox.Show(this,
                $"'{vm.Title}' has unsaved changes.\n\nSave before closing?",
                "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Cancel) return;

            if (choice == MessageBoxResult.Yes)
            {
                string? error = await vm.SaveAsync();
                if (error is not null)
                {
                    Warn(error);
                    return;
                }
            }
        }

        onRemoved();
        MainTabs.Items.Remove(tab);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (GameInstall.TryOpen(_vm.Config.GamePath, out _) is null && !PromptForGameFolder())
        {
            Close();
            return;
        }

        // Independent of the game install (a save lives in Documents, not the install folder) - runs
        // alongside InitializeAsync rather than waiting on it.
        _ = _vm.LoadSavesAsync();

        await _vm.InitializeAsync();

        if (_vm.ArchiveHashMismatches.Count > 0)
        {
            Warn("Some of this install's game files don't match the known hashes for a clean, " +
                 "Steam-patched-to-1.03 Far Cry 2:\n\n" +
                 string.Join('\n', _vm.ArchiveHashMismatches) +
                 "\n\nThis usually means a different game version, a corrupted download, or files " +
                 "already modified by something else. JackAll will still work, but its \"vanilla\" " +
                 "baseline may not be what you expect - verifying game files in Steam is the safest fix.");
        }
    }

    /// <summary>First run: find the game, or there is nothing to manage.</summary>
    private bool PromptForGameFolder()
    {
        while (true)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Where is Far Cry 2 installed?",
                InitialDirectory = @"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            if (GameInstall.TryOpen(dialog.FolderName, out string error) is not null)
            {
                _vm.Config.GamePath = dialog.FolderName;
                _vm.Config.Save();
                return true;
            }

            if (MessageBox.Show(this, $"{error}\n\nTry another folder?", "Not a Far Cry 2 folder",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return false;
            }
        }
    }

    // ----------------------------------------------------------------- Mods tab

    /// <summary>Opens the selected mod's containing folder - the workspace's own staging folder for
    /// that row, or the folder holding the zip for any other.</summary>
    private void OpenModLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMod is not { } mod) return;

        string folder = mod.IsWorkspace
            ? AppConfig.WorkspaceDir
            : Path.GetDirectoryName(((ZipModLayer)mod.Layer).ZipPath)!;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a mod",
            Filter = "Mod archives (*.zip)|*.zip",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        foreach (string path in dialog.FileNames)
        {
            try
            {
                var layer = new ZipModLayer(path);

                if (layer.Hashes.Count == 0)
                {
                    Warn($"'{Path.GetFileName(path)}' has no files this game recognises.\n\n" +
                         "A mod zip should contain the game's own folder structure - for example " +
                         "worlds\\world1\\generated\\… - which is the layout you get from unpacking an archive.");
                    continue;
                }

                // The workspace row is pinned last, so new mods go in above it.
                int insertAt = _vm.Mods.Count(m => !m.IsWorkspace);
                _vm.Mods.Insert(insertAt, new ModRow(layer, isWorkspace: false));
            }
            catch (Exception ex)
            {
                Warn($"Couldn't read '{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        _vm.Reindex();
        _vm.SaveConfig();
    }

    /// <summary>
    /// "Import legacy…": picks a zip carrying a full replacement patch.dat/patch.fat (the old
    /// build_patch.bat-style workflow) and stages only what it actually changed relative to the base
    /// game straight into the workspace - see <see cref="MainViewModel.ImportLegacyMod"/>. Unlike
    /// <see cref="AddMod_Click"/>, this doesn't add a new row to the Mods grid: the result becomes your
    /// own workspace edits, ready to zip up and share once you're happy with it.
    /// </summary>
    private async void ImportLegacy_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a legacy mod (a zip containing patch.dat and patch.fat)",
            Filter = "Mod archives (*.zip)|*.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        _vm.Busy = true;
        try
        {
            LegacyImportResult result = await _vm.ImportLegacyMod(dialog.FileName);

            string fragmentsNote = result.FragmentsImported > 0
                ? $" + {result.FragmentsImported:N0} entity-library fragments"
                : string.Empty;
            _vm.Status =
                $"Imported '{Path.GetFileName(dialog.FileName)}': {result.Imported:N0} changed files{fragmentsNote} " +
                $"staged to your workspace ({result.Skipped:N0} of {result.TotalEntries:N0} were identical to the " +
                "base game and left out). Open workspace to review, then zip it up to share.";
        }
        catch (Exception ex)
        {
            Warn($"Couldn't import that mod: {ex.Message}");
        }
        finally
        {
            _vm.Busy = false;
        }
    }

    private void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMod() is not { IsWorkspace: false } row)
        {
            Warn("Pick a mod to remove. (The workspace holds your own edits and can't be removed - " +
                 "switch it off instead.)");
            return;
        }

        _vm.Mods.Remove(row);
        _vm.Reindex();
        _vm.SaveConfig();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedMod(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedMod(+1);

    private void MoveSelectedMod(int delta)
    {
        if (SelectedMod() is not { IsWorkspace: false } row) return;

        int from = _vm.Mods.IndexOf(row);
        int to = from + delta;

        // The workspace is always last; nothing may be moved past it.
        int lastMovable = _vm.Mods.Count(m => !m.IsWorkspace) - 1;
        if (to < 0 || to > lastMovable) return;

        _vm.Mods.Move(from, to);
        ModGrid.SelectedItem = row;
        _vm.Reindex();
        _vm.SaveConfig();
    }

    private async void BuildAndApply_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Install is null) return;

        _vm.Busy = true;
        _vm.Status = "Building patch.dat…";
        try
        {
            BuildResult result = await _vm.BuildPatch();

            _vm.Status =
                $"Built patch.dat - {result.TotalEntries:N0} files "
                + $"({result.OverriddenEntries:N0} replaced, {result.AddedEntries:N0} added, "
                + $"{MainViewModel.FormatSize(result.OutputBytes)}). Launch the game to see it.";

            _vm.SaveConfig();
        }
        catch (Exception ex)
        {
            _vm.Status = "Build failed - the game's files were not changed.";
            MessageBox.Show(this,
                $"{ex.Message}\n\nYour game is untouched: the new patch is written to a temporary " +
                "file and only swapped in once it's complete.",
                "Build failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.Busy = false;
        }
    }

    private void RestoreVanilla_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Install is not { } install) return;

        if (!install.HasVanillaBackup)
        {
            Warn("There's no backup yet - nothing has been built, so the game is already unmodded.");
            return;
        }

        if (MessageBox.Show(this,
                "Remove every mod from the game and put its original files back?\n\n" +
                "Your mods and your workspace stay exactly where they are here in JackAll - this only un-applies them from the game.",
                "Remove all mods", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        install.RestoreVanilla();
        _vm.ReloadPatchArchive();

        IReadOnlyList<string> patchMismatches = VanillaHashes.Load(AppConfig.VanillaHashesFile)
            .FindMismatches(install.DataDir, install.PatchArchiveRelativePaths());
        if (patchMismatches.Count > 0)
        {
            Warn("All mods were removed, but the restored patch.dat/patch.fat still don't match the " +
                 "known hash for a clean 1.03 Far Cry 2. This can happen if the backup JackAll made " +
                 "was already modded before this tool ever saw it, or if your game is a different " +
                 "version. Verifying game files in Steam is the safest way back to a truly clean install.");
        }

        _vm.Status = "All mods removed - the game's original files are back. Your mods are still listed here.";
    }

    private ModRow? SelectedMod() => ModGrid.SelectedItem as ModRow;

    private void ModGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.SelectedMod = ModGrid.SelectedItem as ModRow;

    // ---------------------------------------------------------------- Files tab

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
    /// Expands or collapses a folder on a single click anywhere on its row, like VS Code's explorer.
    /// Selection is left to the TreeView's own handling, which runs right after this.
    /// </summary>
    private void FolderTree_ItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item || !item.HasItems)
        {
            return;
        }

        // A preview event tunnels through every ancestor item on the way down, so each one of them
        // sees this click. Only the innermost — the row actually under the cursor — should act.
        if (Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) != item)
        {
            return;
        }

        // The chevron already toggles itself; toggling again here would cancel it out.
        if (Ancestor<ToggleButton>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        item.IsExpanded = !item.IsExpanded;
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        return node as T;
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

    // ---------------------------------------------------------------- Saves tab

    private void SavesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.SelectedSave = SavesGrid.SelectedItem as SaveRow;

    private void OpenSaveXmlEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSave is not { } save || _vm.SelectedSaveDetails?.DocumentXml is not { } xml) return;
        OpenSaveXmlEditorTab(save, xml);
    }
}
