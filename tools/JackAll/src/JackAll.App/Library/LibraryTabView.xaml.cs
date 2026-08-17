using System.Windows;
using System.Windows.Controls;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Core.Vfs;
using JackAll.Tools.World;

namespace JackAll.App.Library;

/// <summary>One library in the selected archetype's chain, and whether the engine reads it.</summary>
public sealed record ChainRow(ArchetypeDefinition Definition, bool Wins, string? OverriddenBy)
{
    public string ShortName => Definition.Layer.ShortName;
    public string Path => Definition.Layer.Path;
    public string? FragmentId => Definition.FragmentId;
    public bool IsUnconfirmed => !Definition.Layer.IsConfirmed;
    public string Verdict => Wins ? "the game reads this" : "dead";
}

/// <summary>
/// The Library tab: the archetype namespace resolved the way the engine resolves it, so a definition
/// some later library overrides is visible as dead instead of looking editable. Editing reuses the
/// ordinary FCB editor, hosted here against the fragment the selected declaration lives in.
/// </summary>
public partial class LibraryTabView : UserControl
{
    private MainViewModel? _vm;
    private ArchetypeIndex? _index;
    private ArchetypeTreeNode? _root;

    /// <summary>Fragment rows per library container, kept because resolving one costs a pass over the
    /// whole VFS index and a click shouldn't pay for that.</summary>
    private readonly Dictionary<uint, IReadOnlyDictionary<string, VfsFile>> _fragments = [];

    public LibraryTabView() => InitializeComponent();

    /// <summary>Called by MainWindow once the VFS is loaded and its worlds become discoverable.</summary>
    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        IReadOnlyList<string> worlds = ArchetypeIndex.DiscoverWorlds(vm.AllKnownPaths);

        WorldPicker.ItemsSource = worlds;
        WorldPicker.SelectedIndex = 0;
        WorldPicker.IsEnabled = worlds.Count > 0;
        LoadButton.IsEnabled = worlds.Count > 0;
        StatusText.Text = worlds.Count > 0
            ? $"{worlds.Count} worlds - pick one and Load"
            : "No entity libraries found";
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is not { } vm || WorldPicker.SelectedItem is not string world) return;

        LoadButton.IsEnabled = false;
        try
        {
            LibraryProfile profile = ProfilePicker.SelectedIndex == 0 ? LibraryProfile.Client : LibraryProfile.Server;
            IProgress<string> progress = new Progress<string>(s => StatusText.Text = s);
            IReadOnlyList<string> dlc = ArchetypeIndex.DiscoverDlcLibraries(vm.AllKnownPaths);

            (ArchetypeIndex index, ArchetypeTreeNode root) = await Task.Run(() =>
            {
                ArchetypeIndex loaded = ArchetypeIndex.Load(world, vm.ReadByPath, progress, profile, dlc);
                return (loaded, ArchetypeTreeNode.Build(loaded));
            });

            _index = index;
            _root = root;
            _fragments.Clear();
            ClearSelection();

            ArchetypeTree.ItemsSource = root.Children;
            ApplyFilter();
            StatusText.Text =
                $"{index.Count:N0} archetypes over {index.Layers.Count} libraries, "
                + $"{index.Overridden.Count():N0} overridden";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't load {world}: {ex.Message}";
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_root is null) return;

        foreach (ArchetypeTreeNode child in _root.Children)
        {
            ArchetypeTreeNode.ApplyFilter(child, SearchBox.Text.Trim(), ShadowedOnly.IsChecked == true);
        }
    }

    private void ArchetypeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_index is null || e.NewValue is not ArchetypeTreeNode { FullName: { } name })
        {
            ClearSelection();
            return;
        }

        IReadOnlyList<ArchetypeDefinition> chain = _index.DefinitionsOf(name);
        string winner = chain[^1].Layer.Path;
        ChainList.ItemsSource = chain
            .Select((definition, i) => new ChainRow(definition, i == chain.Count - 1, winner))
            .ToList();

        // Opens on the definition the engine reads; the shadowed ones stay one click away.
        ChainList.SelectedIndex = chain.Count - 1;
    }

    private void ChainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChainList.SelectedItem is ChainRow row)
        {
            ShowDeclaration(row);
        }
    }

    private void ClearSelection()
    {
        ChainList.ItemsSource = null;
        EditorHost.Content = null;
        EditorPlaceholder.Visibility = Visibility.Visible;
    }

    /// <summary>Loads the fragment this declaration lives in, positioned on the declaration itself.</summary>
    private void ShowDeclaration(ChainRow row)
    {
        if (_vm is not { } vm) return;

        if (FindFragment(vm, row.Definition) is not { } fragment)
        {
            StatusText.Text = $"{row.Path} has no fragment row for {row.FragmentId}";
            return;
        }

        try
        {
            string xml = AppText.DecodeUtf8(vm.Read(fragment));
            var editor = new FcbEditorTabViewModel(
                fragment.FileName, fragment.Hash, xml, vm.ReadOriginalFragment(fragment),
                FcbDefinitionsProvider.Value.Value, vm.StageFragmentEdits(fragment))
            {
                Notice = row.Wins
                    ? null
                    : $"'{row.Definition.Name}' is declared again by {row.OverriddenBy}, which loads later. "
                      + "That copy is what the game reads, so editing this one changes the file and nothing in game.",
            };

            EditorHost.Content = new FcbEditorTabView(editor);
            EditorPlaceholder.Visibility = Visibility.Collapsed;
            editor.TryReveal(WorldHashes.HidName, row.Definition.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't open {fragment.FileName}: {ex.Message}";
        }
    }

    private VfsFile? FindFragment(MainViewModel vm, ArchetypeDefinition definition)
    {
        if (definition.FragmentId is not { } fragmentId)
        {
            return vm.FindByHash(definition.ContainerHash);
        }

        if (!_fragments.TryGetValue(definition.ContainerHash, out IReadOnlyDictionary<string, VfsFile>? byId))
        {
            _fragments[definition.ContainerHash] = byId = vm.FragmentsOf(definition.ContainerHash);
        }
        return byId.GetValueOrDefault(fragmentId);
    }
}
