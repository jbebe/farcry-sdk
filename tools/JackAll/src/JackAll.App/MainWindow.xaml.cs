using JackAll.App.FileHandlers;
using JackAll.App.FileHandlers.Mgb;
using JackAll.App.Picker;
using JackAll.Core.Format.Rml;
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

    public static readonly RoutedCommand CloseDocumentTabCommand = new();
    private static readonly RoutedCommand SelectTabCommand = new();

    public MainWindow()
    {
        // Before the first element loads, so nothing is ever painted in the wrong theme.
        ThemeManager.Apply(_vm.Config.Theme);
        InitializeComponent();
        ThemePicker.ItemsSource = Enum.GetValues<AppTheme>();
        ThemePicker.SelectedItem = _vm.Config.Theme;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        DataContext = _vm;
        MoveTab.Attach(_vm);
        AiTab.Attach(_vm);
        foreach ((TabItem item, ISavableTab tab) in SavableTabs)
        {
            tab.DirtyChanged += () => ItemState.SetIsChanged(item, tab.IsDirty);
        }
        Loaded += OnLoaded;
        Closing += OnClosing;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        CommandBindings.Add(new CommandBinding(
            NavigateBackCommand,
            (_, _) => _vm.NavigateBack(),
            (_, e) => e.CanExecute = _vm.CanNavigateBack));
        CommandBindings.Add(new CommandBinding(
            NavigateForwardCommand,
            (_, _) => _vm.NavigateForward(),
            (_, e) => e.CanExecute = _vm.CanNavigateForward));
        CommandBindings.Add(new CommandBinding(CloseDocumentTabCommand, (_, e) => CloseDocumentTab(e.Parameter as TabItem)));
        CommandBindings.Add(new CommandBinding(SelectTabCommand, (_, e) => MainTabs.SelectedIndex = (int)e.Parameter));

        InputBindings.Add(new KeyBinding(CloseDocumentTabCommand, Key.W, ModifierKeys.Control));
        // Ctrl+1..N pick the fixed tabs, the ones before the document divider.
        for (int i = 0; i < Math.Min(MainTabs.Items.IndexOf(DocumentDivider), 9); i++)
        {
            InputBindings.Add(new KeyBinding(SelectTabCommand, Key.D1 + i, ModifierKeys.Control) { CommandParameter = i });
        }
    }

    private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = (AppTheme)ThemePicker.SelectedItem;
        if (theme == _vm.Config.Theme) return;

        _vm.Config.Theme = theme;
        ThemeManager.Apply(theme);

        // Config.Save, not SaveConfig: that one rebuilds the mod list from rows that may not be loaded yet.
        _vm.Config.Save();
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
            XrefsPanel.Show(_vm, _vm.SelectedFile);
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

        // Null when a level was virtualized out of existence or the tree changed underneath us.
        if (TreeViewBehaviors.RealizePath(FolderTree, _vm.GetAncestorChain(target), c => c.IsExpanded = true)
            is not { } item)
        {
            return;
        }

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

    /// <summary>The path the open preview just staged an edit to. The reindex staging causes selects
    /// that path again, and the preview, which already shows what it staged, is kept, not rebuilt.</summary>
    private string? _stagedByPreview;

    /// <summary>Asks FileHandlerCatalog for the view that matches the selected file's type, if any.</summary>
    private void RefreshPreview()
    {
        VfsFile? file = _vm.SelectedFile;
        bool keep = file is not null && PreviewHost.Content is not null
            && string.Equals(file.Path, _stagedByPreview, StringComparison.OrdinalIgnoreCase);
        _stagedByPreview = null;
        if (keep)
        {
            XrefsPanel.Show(_vm, file);
            return;
        }

        UserControl? view = file is not null
            ? FileHandlerCatalog.CreateView(
                file, () => _vm.Read(file), bytes =>
                {
                    if (ReplaceGuarded(file, bytes))
                    {
                        _stagedByPreview = file.Path;
                    }
                }, () => OpenFcbEditorTab(file),
                () => _vm.ReadOriginal(file), id => _vm.ResolveSoundResource(id), _vm.NavigateTo, () => OpenDominoEditorTab(file),
                () => OpenMgbEditorTab(file), bank => _vm.FindRigs(file.Path, bank))
            : null;

        PreviewHost.Content = view;
        PreviewHost.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
        NoPreviewPanel.Visibility = file is not null && view is null ? Visibility.Visible : Visibility.Collapsed;

        // Outside the catalog switch above on purpose - the xref lists are worth showing even for a
        // file whose type has no handler, which is exactly when they're the only thing on offer.
        XrefsPanel.Show(_vm, file);
    }

    /// <summary>Set once the unsaved-edits prompt has been answered, so the second close goes through.</summary>
    private bool _closeConfirmed;

    /// <summary>The fixed tabs that hold unsaved edits of their own, beside the tab item that shows them.</summary>
    private (TabItem Item, ISavableTab Tab)[] SavableTabs => [(MoveTabItem, MoveTab), (AiTabItem, AiTab)];

    /// <summary>
    /// Fixed tabs have no close of their own, so their unsaved edits are asked about here. Saving is
    /// asynchronous, so the first close is cancelled and repeated once it is done.
    /// </summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        List<(TabItem Item, ISavableTab Tab)> dirty = [.. SavableTabs.Where(t => t.Tab.IsDirty)];
        if (!_closeConfirmed && dirty.Count > 0)
        {
            e.Cancel = true;
            MessageBoxResult choice = MessageBox.Show(this,
                $"The {string.Join(" and ", dirty.Select(d => d.Item.Header))} tab has unsaved changes.\n\nSave before closing?",
                "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            if (choice == MessageBoxResult.Yes)
            {
                foreach ((TabItem _, ISavableTab tab) in dirty)
                {
                    if (await tab.SaveAsync() is { } error)
                    {
                        MessageBox.Show(this, error, "Not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            _closeConfirmed = true;
            Close();
            return;
        }

        _vm.SaveConfig();
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
        FilePicker.UseSource(_vm);

        await _vm.InitializeAsync();
        await MapTab.InitializeAsync(_vm);
        LibraryTab.Initialize(_vm);
        MoveTab.Initialize();
        await AiTab.InitializeAsync();

        // The Map tab owns neither the Library tab nor the editor registry, so it asks.
        MapTab.ArchetypeRequested += async (world, archetype) =>
        {
            MainTabs.SelectedItem = LibraryTabItem;
            await LibraryTab.RevealArchetype(world, archetype);
        };
        MapTab.SectorEditorRequested += OpenSectorEditorTab;
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
