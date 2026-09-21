using JackAll.App.FileHandlers.Domino;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.App.FileHandlers.Mgb;
using JackAll.App.FileHandlers.Sav;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.Sav;
using JackAll.Tools.World;
using System.Windows.Controls;
using System.Windows;

namespace JackAll.App;

/// <summary>The editor tabs MainWindow opens next to the three static ones: fragment XML, save-game
/// tree, Domino graph, and Magma UI package - each with its own open-or-focus registry.</summary>
public partial class MainWindow
{
    /// <summary>
    /// The shared open-or-focus flow behind all four editor-tab registries: focus the already-open
    /// tab for <paramref name="key"/> when there is one, otherwise have <paramref name="createTab"/>
    /// build a fully-wired tab and register/show it. <paramref name="createTab"/> receives the action
    /// that removes this key from <paramref name="registry"/>, for its close handler to call; a null
    /// return means opening failed (and the user was already told), so nothing is added.
    /// </summary>
    private void OpenOrFocusEditorTab<TKey>(
        Dictionary<TKey, TabItem> registry, TKey key, Func<Action, TabItem?> createTab)
        where TKey : notnull
    {
        if (registry.TryGetValue(key, out TabItem? existing))
        {
            MainTabs.SelectedItem = existing;
            return;
        }

        TabItem? tab = createTab(() => registry.Remove(key));
        if (tab is null) return;

        registry[key] = tab;
        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
    }

    // ------------------------------------------------------------ fragment XML editor tabs

    /// <summary>Open editor tabs, keyed by the fragment's own VFS key - lets "Open in FCB Editor…" just
    /// focus an already-open tab instead of opening a second copy of the same content.</summary>
    private readonly Dictionary<ulong, TabItem> _openEditors = [];

    private void OpenFcbEditorTab(VfsFile file)
        => OpenOrFocusEditorTab(_openEditors, file.Hash, onRemoved =>
        {
            FcbDocumentViewModel vm;
            try
            {
                vm = _vm.OpenFragmentDocument(file);
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{file.FileName}': {ex.Message}");
                return null;
            }
            vm.Notice = _vm.LayerMismatchNoteFor(file);
            return DocumentTab(vm, onRemoved);
        });

    private TabItem DocumentTab(FcbDocumentViewModel vm, Action onRemoved)
    {
        var tab = new TabItem { Content = new FcbDocumentView(vm) };
        tab.Header = BuildClosableTabHeader(tab, vm, onRemoved);
        return tab;
    }

    /// <summary>
    /// The Map tab's "Open entity in XML editor": worldsector containers split into one fragment per
    /// placed entity (see <c>FcbFragments</c>), so this opens just the entity's own override unit —
    /// saving stages that one entity, and two mods editing different entities of the same sector no
    /// longer conflict. Falls back to the whole <c>worldsector*.data.fcb</c>, positioned on the
    /// entity, when no fragment row exists for it (an entity with no <c>disEntityId</c>, or the
    /// background fragment pass hasn't reached this sector yet) — that path stages the whole sector.
    /// </summary>
    private void OpenSectorEditorTab(string sectorPath, ulong entityId)
    {
        if (_vm.FindByHash(NameHash.Compute(sectorPath)) is not { } file)
        {
            Warn($"'{sectorPath}' isn't in the merged filesystem.");
            return;
        }

        if (_vm.FindFragment(file.EngineHash, FcbFragments.EntityFragmentId(entityId)) is { } entityFragment)
        {
            OpenFcbEditorTab(entityFragment);
            return;
        }

        OpenOrFocusEditorTab(_openEditors, file.Hash, onRemoved =>
        {
            try
            {
                return DocumentTab(_vm.OpenContainerDocument(file), onRemoved);
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{file.FileName}': {ex.Message}");
                return null;
            }
        });

        // After the open-or-focus, so picking a second entity in a sector that is already open still
        // moves to it rather than just raising the tab.
        if (_openEditors.TryGetValue(file.Hash, out TabItem? open)
            && open.Content is FcbDocumentView editor)
        {
            editor.ViewModel.TryReveal(WorldHashes.DisEntityId, BitConverter.GetBytes(entityId));
        }
    }

    /// <summary>Open save-tree editor tabs, keyed by the save's own file path - same
    /// dedup-by-focusing-the-existing-tab behavior as <see cref="_openEditors"/>, just keyed by path
    /// since a save has no <c>VfsFile.Hash</c> of its own.</summary>
    private readonly Dictionary<string, TabItem> _openSaveEditors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Saves tab's "Open in FCB Editor…": the save's <c>PersistenceDB</c> in the same editor
    /// as any document. Save writes the player's real <c>.sav</c> in place, with no backup.</summary>
    private void OpenSaveFcbEditorTab(SaveRow save)
        => OpenOrFocusEditorTab(_openSaveEditors, save.Info.FilePath, onRemoved =>
        {
            FcbObject root;
            try
            {
                root = SaveGameDocument.ReadFcbRoot(save.Info);
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{save.FileName}': {ex.Message}");
                return null;
            }

            string world = save.Info.WorldName;
            var entities = new SaveGameBases(
                world, id => _vm.PlacedEntitiesOf(world).GetAwaiter().GetResult().GetValueOrDefault(id), _vm.ArchetypeLookup(world));
            return DocumentTab(new FcbDocumentViewModel(
                save.FileName, root, baseline: null, new FcbEditContext(SaveGameNames.Shared.Value),
                persist: async edited =>
                {
                    try
                    {
                        await Task.Run(() => SaveGameDocument.WriteFcbRoot(save.Info, edited, save.Info.FilePath));
                        _vm.RefreshSaveRow(save.Info.FilePath);
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return $"Couldn't write '{save.FileName}' back to disk: {ex.Message}";
                    }
                },
                entities), onRemoved);
        });

    // ------------------------------------------------------------ domino graph editor tabs

    /// <summary>Open Domino editor tabs, keyed by the file's own hash - same dedup-by-focusing-the-
    /// existing-tab behavior as <see cref="_openEditors"/>. No dirty-tracking to plumb through here:
    /// unlike the XML editor, there's no write path yet, so a Domino tab is pure view.</summary>
    private readonly Dictionary<ulong, TabItem> _openDominoEditors = [];

    private void OpenDominoEditorTab(VfsFile file)
        => OpenOrFocusEditorTab(_openDominoEditors, file.Hash, onRemoved =>
        {
            string source;
            try
            {
                source = AppText.DecodeUtf8(_vm.Read(file));
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{file.FileName}': {ex.Message}");
                return null;
            }

            var vm = new DominoTabViewModel(file.FileName, source, file.NameIsKnown ? file.Path : null, DominoServices);
            var view = new DominoTabView(vm);
            var tab = new TabItem { Content = view };
            // No dirty-tracking wrapper like the XML and MGB editors get: this tab is read-only, so
            // there is never anything to prompt about on the way out.
            tab.Header = BuildClosableTabHeader(vm.Title, () =>
            {
                onRemoved();
                MainTabs.Items.Remove(tab);
            }, out _);
            return tab;
        });

    private DominoServices? _dominoServices;

    private DominoServices DominoServices => _dominoServices ??= new(
        _vm.ReadByPath,
        _vm.Read,
        path => _vm.FindByHash(NameHash.Compute(path)),
        _vm.ResolveSoundResource,
        _vm.FindEntityFragment,
        file =>
        {
            MainTabs.SelectedItem = FilesTabItem;
            _vm.NavigateTo(file);
        },
        OpenFcbEditorTab,
        OpenDominoEditorTab);

    // ------------------------------------------------------------ mgb package editor tabs

    /// <summary>Open Magma UI package editor tabs, keyed by the file's own hash - same
    /// dedup-by-focusing-the-existing-tab behavior as <see cref="_openEditors"/>.</summary>
    private readonly Dictionary<ulong, TabItem> _openMgbEditors = [];

    /// <summary>The Files tab's "Open in MGB Editor…" launcher. Unlike the fragment and save editors
    /// there's no XML in between: <see cref="MgbTabView"/> edits the decoded package model directly and
    /// its own Save reserialises it straight into the workspace via <see cref="MainViewModel.Replace"/>.</summary>
    private void OpenMgbEditorTab(VfsFile file)
        => OpenOrFocusEditorTab(_openMgbEditors, file.Hash, onRemoved =>
        {
            byte[] content;
            try
            {
                content = _vm.Read(file);
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{file.FileName}': {ex.Message}");
                return null;
            }

            var view = new MgbTabView(file.FileName, content, bytes => ReplaceGuarded(file, bytes), _vm.ReadByPath);
            var tab = new TabItem { Content = view };
            tab.Header = BuildClosableTabHeader(view.Title,
                () => CloseMgbEditorTab(tab, view, onRemoved),
                out TextBlock title);
            view.DirtyChanged += () => title.Text = view.IsDirty ? $"{view.Title} *" : view.Title;
            return tab;
        });

    /// <summary>The <see cref="MgbTabView"/> counterpart to <see cref="CloseEditorTabAsync"/>: same
    /// prompt, same leave-the-tab-open-on-failure rule, just against the package editor's own
    /// synchronous <see cref="MgbTabView.Save"/>.</summary>
    private void CloseMgbEditorTab(TabItem tab, MgbTabView view, Action onRemoved)
    {
        if (view.IsDirty)
        {
            MessageBoxResult choice = MessageBox.Show(this,
                $"'{view.Title}' has unsaved changes.\n\nSave before closing?",
                "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Cancel) return;

            if (choice == MessageBoxResult.Yes && view.Save() is { } error)
            {
                Warn(error);
                return;
            }
        }

        onRemoved();
        MainTabs.Items.Remove(tab);
    }

    // ------------------------------------------------------------ tab chrome

    /// <summary>Title plus a small "×" close button, since the three static tabs (Mods/Saves/Files) are
    /// the only ones that don't need one - matches the plain-code-behind tab management above rather
    /// than pulling in a DataTemplate/ItemsSource restructuring for a TabControl that otherwise stays as
    /// declared in XAML. <paramref name="titleText"/> comes back out so a caller whose content tracks
    /// unsaved changes can retitle it; a read-only tab just discards it.</summary>
    private static FrameworkElement BuildClosableTabHeader(string title, Action onClose, out TextBlock titleText)
    {
        titleText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
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
        close.Click += (_, _) => onClose();

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(titleText);
        panel.Children.Add(close);
        return panel;
    }

    /// <summary>The XML editor's header: <see cref="BuildClosableTabHeader"/> plus the dirty marker and
    /// the unsaved-changes prompt its two (fragment and savegame) tab flavours both need.</summary>
    private FrameworkElement BuildClosableTabHeader(TabItem tab, FcbDocumentViewModel vm, Action onRemoved)
    {
        FrameworkElement header = BuildClosableTabHeader(vm.Title,
            async () => await CloseEditorTabAsync(tab, vm, onRemoved),
            out TextBlock title);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FcbDocumentViewModel.IsDirty))
            {
                title.Text = vm.IsDirty ? $"{vm.Title} *" : vm.Title;
            }
        };
        return header;
    }

    /// <summary>Prompts for unsaved changes before closing - Save runs the exact same
    /// <see cref="FcbDocumentViewModel.SaveAsync"/> path as the tab's own Save button. A failed save
    /// leaves the tab open rather than closing anyway, so a bad edit is never silently discarded.</summary>
    private async Task CloseEditorTabAsync(TabItem tab, FcbDocumentViewModel vm, Action onRemoved)
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
}
