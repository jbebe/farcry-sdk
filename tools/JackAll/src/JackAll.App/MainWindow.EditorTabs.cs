using JackAll.App.FileHandlers.Domino;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.App.FileHandlers.Mgb;
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
            string xml;
            string? originalXml;
            try
            {
                xml = AppText.DecodeUtf8(_vm.Read(file));
                // Null for a mod-added fragment (no archive provides its container) - nothing to diff
                // against, so every value in it just reads as unremarkable base content.
                originalXml = _vm.ReadOriginalFragment(file);
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{file.FileName}': {ex.Message}");
                return null;
            }

            var vm = new FcbEditorTabViewModel(
                file.FileName, file.Hash, xml, originalXml, FcbDefinitionsProvider.Value.Value,
                _vm.StageFragmentEdits(file));
            var view = new FcbEditorTabView(vm);
            var tab = new TabItem { Content = view };
            tab.Header = BuildClosableTabHeader(tab, vm, onRemoved);
            return tab;
        });

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
            FcbObject root;
            FcbObject? vanilla;
            try
            {
                // A container is binary, not a fragment's XML - parse it, and never round-trip it
                // through XML, which is neither what it is on disk nor what has to be written back.
                root = FcbDocument.Deserialize(_vm.Read(file));
                vanilla = _vm.ReadOriginal(file) is { } original ? FcbDocument.Deserialize(original) : null;
            }
            catch (Exception ex)
            {
                Warn($"Couldn't open '{file.FileName}': {ex.Message}");
                return null;
            }

            var vm = new FcbEditorTabViewModel(
                file.FileName, file.Hash, root, vanilla,
                FcbDefinitionsProvider.Value.Value, _vm.StageContainerEdits(file));
            var view = new FcbEditorTabView(vm);
            var tab = new TabItem { Content = view };
            tab.Header = BuildClosableTabHeader(tab, vm, onRemoved);
            return tab;
        });

        // After the open-or-focus, so picking a second entity in a sector that is already open still
        // moves to it rather than just raising the tab.
        if (_openEditors.TryGetValue(file.Hash, out TabItem? open)
            && open.Content is FcbEditorTabView editor)
        {
            editor.ViewModel.TryReveal(WorldHashes.DisEntityId, BitConverter.GetBytes(entityId));
        }
    }

    /// <summary>Open save-tree editor tabs, keyed by the save's own file path - same
    /// dedup-by-focusing-the-existing-tab behavior as <see cref="_openEditors"/>, just keyed by path
    /// since a save has no <c>VfsFile.Hash</c> of its own.</summary>
    private readonly Dictionary<string, TabItem> _openSaveEditors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Saves tab's "Open in FCB Editor…" launcher - same tree/property-grid view the Files
    /// tab's fragments get, and just as editable: Save here writes straight back into <paramref name="save"/>'s
    /// own `.sav` file via <see cref="SaveGameDocument.WriteFcbRoot"/>, in place, no confirmation and no
    /// backup - unlike a mod fragment there's no workspace/deploy step in between, so this really is the
    /// player's real save the moment Save is clicked. <paramref name="documentXml"/> is only ever parsed
    /// to build the tree; what actually gets written back is <c>root</c>, the tree <see cref="FcbEditorTabViewModel"/>
    /// mutates in place as rows are edited - never <paramref name="documentXml"/> itself again.</summary>
    private void OpenSaveFcbEditorTab(SaveRow save, string documentXml)
        => OpenOrFocusEditorTab(_openSaveEditors, save.Info.FilePath, onRemoved =>
        {
            var vm = new FcbEditorTabViewModel(
                save.FileName, hash: 0, documentXml, vanillaXml: null, FcbDefinitionsProvider.Value.Value,
                persist: async root =>
                {
                    try
                    {
                        await Task.Run(() => SaveGameDocument.WriteFcbRoot(save.Info, root, save.Info.FilePath));
                        _vm.RefreshSaveRow(save.Info.FilePath);
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return $"Couldn't write '{save.FileName}' back to disk: {ex.Message}";
                    }
                },
                useSaveGameNameHarvest: true);
            var view = new FcbEditorTabView(vm);
            var tab = new TabItem { Content = view };
            tab.Header = BuildClosableTabHeader(tab, vm, onRemoved);
            return tab;
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

            // The tab resolves two more things through the VFS: every node type the graph refers to (for
            // pin signatures) and the graph's own `*.debug.lua` twin (for the editor's original box and pin
            // names). Both are optional - a null return just means that enrichment is skipped.
            var vm = new DominoTabViewModel(file.FileName, source, file.NameIsKnown ? file.Path : null, ReadDominoText);
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

    /// <summary>Reads a Domino script by its game-relative path, for the node catalog and debug-twin
    /// lookups. Returns null for anything the VFS can't resolve, which is the normal outcome for a mod
    /// that references a node type this install doesn't have.</summary>
    private string? ReadDominoText(string gameRelativePath)
    {
        try
        {
            byte[]? bytes = _vm.ReadByPath(gameRelativePath);
            return bytes is null ? null : AppText.DecodeUtf8(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

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

            var view = new MgbTabView(file.FileName, content, bytes => _vm.Replace(file, bytes), _vm.ReadByPath);
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
    private FrameworkElement BuildClosableTabHeader(TabItem tab, FcbEditorTabViewModel vm, Action onRemoved)
    {
        FrameworkElement header = BuildClosableTabHeader(vm.Title,
            async () => await CloseEditorTabAsync(tab, vm, onRemoved),
            out TextBlock title);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FcbEditorTabViewModel.IsDirty))
            {
                title.Text = vm.IsDirty ? $"{vm.Title} *" : vm.Title;
            }
        };
        return header;
    }

    /// <summary>Prompts for unsaved changes before closing - Save runs the exact same
    /// <see cref="FcbEditorTabViewModel.SaveAsync"/> path as the tab's own Save button. A failed save
    /// leaves the tab open rather than closing anyway, so a bad edit is never silently discarded.</summary>
    private async Task CloseEditorTabAsync(TabItem tab, FcbEditorTabViewModel vm, Action onRemoved)
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
