using System.Windows;
using System.Windows.Controls;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.World;

namespace JackAll.App.Library;

/// <summary>One mod's edit to the selected archetype, and whether it lands on the copy the game reads.</summary>
public sealed record ModRow(string Mod, ArchetypeDefinition Definition, bool IsLive)
{
    public string Path => Definition.Layer.Path;
    public string Verdict => IsLive ? "the game reads this" : "dead";
}

/// <summary>
/// The Library tab: every archetype a world resolves, opened on the definition the game reads, with
/// the mods editing it listed beside it. Editing reuses the ordinary FCB editor, hosted here against
/// the fragment the declaration lives in.
/// </summary>
public partial class LibraryTabView : UserControl
{
    private MainViewModel? _vm;
    private ArchetypeIndex? _index;
    private StagedEdits _edits = new([]);
    private ArchetypeTreeNode? _root;
    private string? _loadedWorld;

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

    /// <summary>
    /// Selects one archetype, loading <paramref name="world"/> first when the tab is showing a
    /// different one - the Map tab's jump lands here, and the user should not have to press Load.
    /// </summary>
    public async Task RevealArchetype(string world, string archetype)
    {
        if (_vm is null) return;

        if (_index is null || !string.Equals(_loadedWorld, world, StringComparison.OrdinalIgnoreCase))
        {
            WorldPicker.SelectedItem = WorldPicker.Items
                .Cast<string>()
                .FirstOrDefault(w => w.Equals(world, StringComparison.OrdinalIgnoreCase));
            await LoadSelectedWorld();
        }

        if (_root is null) return;

        if (ArchetypeTreeNode.Reveal(_root, archetype) is null)
        {
            StatusText.Text = $"'{archetype}' is not declared by {world}'s libraries";
        }
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadSelectedWorld();

    private async Task LoadSelectedWorld()
    {
        if (_vm is not { } vm || WorldPicker.SelectedItem is not string world) return;

        LoadButton.IsEnabled = false;
        try
        {
            IProgress<string> progress = new Progress<string>(s => StatusText.Text = s);
            ArchetypeIndex index = await vm.ArchetypesOf(world, progress);
            var edits = new StagedEdits(vm.Layers);
            (ArchetypeTreeNode root, int modded) = await Task.Run(() =>
            {
                ArchetypeTreeNode built = ArchetypeTreeNode.Build(index, edits);
                return (built, built.Archetypes().Count(a => a.IsModded));
            });

            _index = index;
            _edits = edits;
            _root = root;
            _loadedWorld = world;
            _fragments.Clear();
            ClearSelection();

            ArchetypeTree.ItemsSource = root.Children;
            ApplyFilter();
            StatusText.Text = $"{index.Count:N0} archetypes, {modded:N0} edited by mods";
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
            ArchetypeTreeNode.ApplyFilter(child, SearchBox.Text.Trim(), ModdedOnly.IsChecked == true);
        }
    }

    private void ArchetypeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_index is null || e.NewValue is not ArchetypeTreeNode { FullName: { } name })
        {
            ClearSelection();
            return;
        }

        IReadOnlyList<ArchetypeDefinition> declarations = _index.DefinitionsOf(name);
        List<ModRow> mods =
        [
            .. declarations.SelectMany(d => _edits.SourcesOf(d).Select(mod => new ModRow(mod, d, d == declarations[^1]))),
        ];
        ModList.ItemsSource = mods;
        ModHint.Text = mods.Count == 0
            ? "No enabled mod edits this archetype."
            : "In load order - a later mod wins where two touch the same field. Click one to open the copy it edits.";

        ShowDeclaration(declarations[^1]);
    }

    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModList.SelectedItem is ModRow row)
        {
            ShowDeclaration(row.Definition);
        }
    }

    private void ClearSelection()
    {
        ModList.ItemsSource = null;
        ModHint.Text = string.Empty;
        EditorHost.Content = null;
        EditorPlaceholder.Visibility = Visibility.Visible;
    }

    /// <summary>Loads the fragment this declaration lives in, positioned on the declaration itself.</summary>
    private void ShowDeclaration(ArchetypeDefinition definition)
    {
        if (_vm is not { } vm || _index?.Winner(definition.Name) is not { } winner) return;

        if (FindFragment(vm, definition) is not { } fragment)
        {
            StatusText.Text = $"{definition.Layer.Path} has no fragment row for {definition.FragmentId}";
            return;
        }

        try
        {
            FcbDocumentViewModel editor = vm.OpenFragmentDocument(fragment);
            editor.Notice = definition == winner
                ? null
                : $"'{definition.Name}' is declared again by {winner.Layer.Path}, which loads later. "
                  + "That copy is what the game reads, so editing this one changes the file and nothing in game.";

            EditorHost.Content = new FcbDocumentView(editor);
            EditorPlaceholder.Visibility = Visibility.Collapsed;
            editor.TryReveal(WorldHashes.HidName, definition.Name);
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
