using JackAll.App.FileHandlers;
using JackAll.App.FileHandlers.Mgb;
using JackAll.Core.Vfs;
using JackAll.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;

namespace JackAll.App;

/// <summary>The window's core: view-model bridge, preview refresh, and startup. Each tab's handlers
/// live in a feature partial (EditorTabs/ModsTab/FilesTab/SavesTab).</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    /// <summary>Alt+Left / Alt+Right, for stepping back and forth through followed references. Static
    /// so the XAML <c>KeyBinding</c>s can name them with <c>x:Static</c>; the handlers are wired per
    /// instance in the constructor.</summary>
    public static readonly RoutedCommand NavigateBackCommand = new();
    public static readonly RoutedCommand NavigateForwardCommand = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
        Closing += (_, _) => _vm.SaveConfig();
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        CommandBindings.Add(new CommandBinding(
            NavigateBackCommand,
            (_, _) => _vm.NavigateBack(),
            (_, e) => e.CanExecute = _vm.CanNavigateBack));
        CommandBindings.Add(new CommandBinding(
            NavigateForwardCommand,
            (_, _) => _vm.NavigateForward(),
            (_, e) => e.CanExecute = _vm.CanNavigateForward));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedFile))
        {
            RefreshPreview();
            RevealSelectedFileInTree();
        }
        else if (e.PropertyName is nameof(MainViewModel.XrefsReady) or nameof(MainViewModel.XrefStatus))
        {
            // The background index just finished (or advanced): the panel is showing a status line
            // for the current file and needs to become the real lists.
            RefreshPreview();
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
                file, () => _vm.Read(file), bytes => _vm.Replace(file, bytes), () => OpenFcbEditorTab(file),
                () => _vm.ReadOriginal(file), _vm.FindByHash, _vm.NavigateTo, () => OpenDominoEditorTab(file),
                () => OpenMgbEditorTab(file))
            : null;

        PreviewHost.Content = view;
        PreviewHost.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
        NoPreviewPanel.Visibility = file is not null && view is null ? Visibility.Visible : Visibility.Collapsed;

        // Outside the catalog switch above on purpose - the xref lists are worth showing even for a
        // file whose type has no handler, which is exactly when they're the only thing on offer.
        XrefsPanel.Show(_vm, file);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (GameInstall.TryOpen(_vm.Config.GamePath, out _) is null && !await PromptForGameFolderAsync())
        {
            Close();
            return;
        }

        // Independent of the game install (a save lives in Documents, not the install folder) - runs
        // alongside InitializeAsync rather than waiting on it.
        _ = _vm.LoadSavesAsync();

        // Points the process-wide oasis string table at the merged filesystem, so a mod that
        // overrides oasisstrings.xml resolves through its version. Nothing is read here - the table
        // parses on the first lookup that wants it (see OasisStringTable's remarks).
        OasisStringTable.UseSource(_vm.ReadByPath);

        await _vm.InitializeAsync();
    }

    /// <summary>First run: find the game, or there is nothing to manage. Also the one and only place
    /// the vanilla-hash check (<see cref="MainViewModel.CheckVanillaHashesAsync"/>) runs - this is the
    /// one moment the folder's identity actually changes, so it's the only moment worth re-verifying
    /// its archives; an already-configured install's files can't change out from under it between one
    /// launch and the next, so <see cref="MainViewModel.InitializeAsync"/> no longer re-hashes them
    /// itself every time.</summary>
    private async Task<bool> PromptForGameFolderAsync()
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

            if (GameInstall.TryOpen(dialog.FolderName, out string error) is { } install)
            {
                _vm.Config.GamePath = dialog.FolderName;
                _vm.Config.Save();

                await _vm.CheckVanillaHashesAsync(install);
                if (_vm.ArchiveHashMismatches.Count > 0)
                {
                    Warn("Some of this install's game files don't match the known hashes for a clean, " +
                         "Steam-patched-to-1.03 Far Cry 2:\n\n" +
                         string.Join('\n', _vm.ArchiveHashMismatches) +
                         "\n\nThis usually means a different game version, a corrupted download, or files " +
                         "already modified by something else. JackAll will still work, but its \"vanilla\" " +
                         "baseline may not be what you expect - verifying game files in Steam is the safest fix.");
                }

                return true;
            }

            if (MessageBox.Show(this, $"{error}\n\nTry another folder?", "Not a Far Cry 2 folder",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return false;
            }
        }
    }
}
