using System.Collections.ObjectModel;
using System.Globalization;
using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.Ai;
using JackAll.Tools.World;

namespace JackAll.App.Ai;

/// <summary>One world's copy of an archetype: the declaration the game reads there, being edited.</summary>
public sealed record SoldierCopy(ArchetypeDefinition Definition, VfsFile Container, FcbObject Root, FcbObject Entity, FcbObject Vanilla, string VanillaXml);

/// <summary>One soldier archetype, with its copy in every single-player world that declares it.</summary>
public sealed class SoldierArchetype(string name, string group, IReadOnlyList<SoldierCopy> copies) : Observable
{
    private bool _isSelected;
    private bool _isEdited;

    public string Name { get; } = name;

    public string Label { get; } = name[(name.IndexOf('.') + 1)..];

    public string Group { get; } = group;

    public IReadOnlyList<SoldierCopy> Copies { get; } = copies;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Changed since the last save.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Differs from the base game.</summary>
    public bool IsEdited { get => _isEdited; set => Set(ref _isEdited, value); }
}

/// <summary>One tunable, showing the value the selected archetypes share and writing to all of them.</summary>
public sealed class SoldierFieldRow(SoldierField field, AiSoldiersModel owner) : Observable
{
    private string _text = "";
    private string _vanilla = "";
    private bool _isMixed;
    private bool _isChanged;

    public SoldierField Field { get; } = field;

    public string Group => Field.Group;

    public string Label => Field.Label;

    public string Help => Field.Help;

    /// <summary>The picks of a choice or yes/no field; null for a number.</summary>
    public IReadOnlyList<string>? Choices { get; } = field.Kind switch
    {
        SoldierFieldKind.Toggle => ["No", "Yes"],
        SoldierFieldKind.Choice => field.Choices,
        _ => null,
    };

    public bool IsNumber => Choices is null;

    public string Text
    {
        get => _text;
        set
        {
            if (value != _text && owner.TryWrite(this, value))
            {
                owner.RefreshRow(this);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public string? Choice
    {
        get => Choices is not null && int.TryParse(_text, out int i) && i >= 0 && i < Choices.Count ? Choices[i] : null;
        set
        {
            if (Choices is not null && value is not null)
            {
                Text = Choices.ToList().IndexOf(value).ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public string Vanilla { get => _vanilla; private set => Set(ref _vanilla, value); }

    public bool IsMixed { get => _isMixed; private set => Set(ref _isMixed, value); }

    /// <summary>At least one selected archetype differs from the base game here.</summary>
    public bool IsChanged { get => _isChanged; private set => Set(ref _isChanged, value); }

    internal void Show(string text, string vanilla, bool mixed, bool changed)
    {
        _text = text;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Choice));
        Vanilla = vanilla;
        IsMixed = mixed;
        IsChanged = changed;
    }
}

/// <summary>
/// The Soldiers view of the AI tab: perception, marksmanship, movement and toughness of every soldier
/// archetype, edited in each single-player world's winning declaration at once.
/// </summary>
public sealed class AiSoldiersModel(MainViewModel vm) : Observable
{
    private IReadOnlyList<SoldierArchetype> _archetypes = [];
    private string _filter = "";
    private bool _isLoaded;

    public ObservableCollection<SoldierArchetype> Visible { get; } = [];

    public IReadOnlyList<SoldierFieldRow> Rows { get; private set; } = [];

    public bool IsLoaded { get => _isLoaded; private set => Set(ref _isLoaded, value); }

    public bool IsDirty => _archetypes.Any(a => a.IsDirty);

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    public int SelectedCount => _archetypes.Count(a => a.IsSelected);

    public string SelectionSummary => SelectedCount switch
    {
        0 => "Tick one or more archetypes on the left.",
        1 => $"Editing {_archetypes.First(a => a.IsSelected).Label}.",
        int n => $"Editing {n} archetypes at once - a value shown blank differs between them; typing one sets it on all.",
    };

    public event Action? DirtyChanged;

    public async Task LoadAsync(IProgress<string> progress)
    {
        List<string> worlds = [.. ArchetypeIndex.DiscoverWorlds(vm.AllKnownPaths).Where(w => w.StartsWith("world", StringComparison.OrdinalIgnoreCase))];
        var copies = new Dictionary<string, List<SoldierCopy>>(StringComparer.Ordinal);
        FcbClassDefinitions definitions = FcbDefinitionsProvider.Value.Value;

        foreach (string world in worlds)
        {
            progress.Report($"Reading {world}'s archetypes…");
            ArchetypeIndex index = await vm.ArchetypesOf(world, progress);
            List<ArchetypeDefinition> soldiers = [.. index.Names
                .Select(index.Winner).OfType<ArchetypeDefinition>()
                .Where(d => d.FragmentId is not null && SoldierFields.IsSoldier(d.Node))];

            foreach (IGrouping<uint, ArchetypeDefinition> library in soldiers.GroupBy(d => d.ContainerHash))
            {
                if (vm.FindByHash(library.Key) is not { } container)
                {
                    continue;
                }
                byte[] merged = vm.Read(container);
                byte[]? original = vm.ReadOriginal(container);
                foreach (SoldierCopy copy in await Task.Run(() => Open(library, container, merged, original, definitions)))
                {
                    (copies.TryGetValue(copy.Definition.Name, out List<SoldierCopy>? list) ? list : copies[copy.Definition.Name] = []).Add(copy);
                }
            }
        }

        _archetypes =
        [
            .. copies
                .Select(p => new SoldierArchetype(p.Key, GroupOf(p.Key), p.Value))
                .OrderBy(a => a.Group == "Enemies" ? 0 : 1)
                .ThenBy(a => a.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
        ];
        foreach (SoldierArchetype archetype in _archetypes)
        {
            archetype.IsEdited = archetype.Copies.Any(c => SoldierFields.All.Any(f => f.Read(c.Entity) != f.Read(c.Vanilla)));
            archetype.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SoldierArchetype.IsSelected))
                {
                    RefreshRows();
                }
            };
        }
        Rows = [.. SoldierFields.All.Select(f => new SoldierFieldRow(f, this))];
        OnPropertyChanged(nameof(Rows));
        ApplyFilter();
        IsLoaded = true;
        progress.Report($"{_archetypes.Count} soldier archetypes across {string.Join(" and ", worlds)}");
    }

    /// <summary>Ticks every visible archetype, or clears them all.</summary>
    public void SelectAll(bool selected)
    {
        foreach (SoldierArchetype archetype in selected ? Visible : _archetypes)
        {
            archetype.IsSelected = selected;
        }
    }

    internal bool TryWrite(SoldierFieldRow row, string text)
    {
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return false;
        }

        bool wrote = false;
        foreach (SoldierArchetype archetype in _archetypes.Where(a => a.IsSelected))
        {
            foreach (SoldierCopy copy in archetype.Copies)
            {
                if (row.Field.Read(copy.Entity) != value && row.Field.Write(copy.Entity, value))
                {
                    archetype.IsDirty = wrote = true;
                }
            }
            archetype.IsEdited = archetype.Copies.Any(c => SoldierFields.All.Any(f => f.Read(c.Entity) != f.Read(c.Vanilla)));
        }
        if (wrote)
        {
            DirtyChanged?.Invoke();
        }
        return wrote;
    }

    internal void RefreshRow(SoldierFieldRow row)
    {
        List<SoldierCopy> copies = [.. _archetypes.Where(a => a.IsSelected).SelectMany(a => a.Copies)];
        if (copies.Count == 0)
        {
            row.Show("", "", false, false);
            return;
        }

        List<double?> values = [.. copies.Select(c => row.Field.Read(c.Entity)).Distinct()];
        List<double?> vanilla = [.. copies.Select(c => row.Field.Read(c.Vanilla)).Distinct()];
        row.Show(
            values.Count == 1 ? Format(values[0]) : "",
            vanilla.Count == 1 ? Format(vanilla[0]) : "varies",
            values.Count > 1,
            copies.Any(c => row.Field.Read(c.Entity) != row.Field.Read(c.Vanilla)));
    }

    /// <summary>Stages every edited archetype's fragment in each world; a copy back to vanilla is unstaged.</summary>
    public async Task<int> SaveAsync()
    {
        FcbClassDefinitions definitions = FcbDefinitionsProvider.Value.Value;
        List<SoldierArchetype> dirty = [.. _archetypes.Where(a => a.IsDirty)];
        var byContainer = await Task.Run(() => dirty
            .SelectMany(a => a.Copies)
            .Select(c => (c.Container, c.Definition.FragmentId!, Xml: FcbXml.ToXml(c.Root, definitions), c.VanillaXml))
            .GroupBy(c => c.Container)
            .ToList());

        foreach (var container in byContainer)
        {
            vm.StageFragments(container.Key, container.Select(c => (c.Item2, c.Xml, c.Xml == c.VanillaXml)));
        }
        foreach (SoldierArchetype archetype in dirty)
        {
            archetype.IsDirty = false;
        }
        DirtyChanged?.Invoke();
        return dirty.Count;
    }

    private void RefreshRows()
    {
        foreach (SoldierFieldRow row in Rows)
        {
            RefreshRow(row);
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private void ApplyFilter()
    {
        string[] words = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Visible.Clear();
        foreach (SoldierArchetype archetype in _archetypes.Where(a => words.All(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
        {
            Visible.Add(archetype);
        }
    }

    /// <summary>Each soldier's prototype out of one library, decoded once, beside its base-game twin.</summary>
    private static List<SoldierCopy> Open(
        IEnumerable<ArchetypeDefinition> soldiers, VfsFile container, byte[] merged, byte[]? original, FcbClassDefinitions definitions)
    {
        FcbObject library = FcbDocument.Deserialize(merged);
        FcbObject vanillaLibrary = original is null ? library.Clone() : FcbDocument.Deserialize(original);
        List<SoldierCopy> copies = [];
        foreach (ArchetypeDefinition definition in soldiers)
        {
            if (FcbFragments.Find(library, definition.FragmentId!) is { } root
                && FcbFragments.Find(vanillaLibrary, definition.FragmentId!) is { } vanillaRoot
                && EntityNamed(root, definition.Name) is { } entity
                && EntityNamed(vanillaRoot, definition.Name) is { } vanilla)
            {
                copies.Add(new SoldierCopy(definition, container, root, entity, vanilla, FcbXml.ToXml(vanillaRoot, definitions)));
            }
        }
        return copies;
    }

    private static FcbObject? EntityNamed(FcbObject node, string name)
    {
        if (node.TypeHash == WorldHashes.Entity && FcbEntityFields.ReadString(node, WorldHashes.HidName) == name)
        {
            return node;
        }
        return node.Children.Select(c => EntityNamed(c, name)).FirstOrDefault(e => e is not null);
    }

    private static string GroupOf(string name) => name.Split('.')[0] switch
    {
        "enemy_archetypes" => "Enemies",
        "buddies" => "Buddies and civilians",
        string other => other,
    };

    private static string Format(double? value)
        => value is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";
}
