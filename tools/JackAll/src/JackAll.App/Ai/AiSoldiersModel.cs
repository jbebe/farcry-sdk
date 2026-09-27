using System.Collections.ObjectModel;
using System.Globalization;
using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.Ai;
using JackAll.Tools.World;

namespace JackAll.App.Ai;

/// <summary>One soldier archetype, with its copy in every single-player world that declares it.</summary>
public sealed class SoldierArchetype(string name, string group, IReadOnlyList<SoldierCopy> copies) : Observable
{
    private bool _isSelected;
    private bool _isEdited = copies.Any(c => c.IsEdited);

    public string Name { get; } = name;

    public string Label { get; } = name[(name.IndexOf('.') + 1)..];

    public string Group { get; } = group;

    public IReadOnlyList<SoldierCopy> Copies { get; } = copies;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Changed since the last save.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Differs from the base game.</summary>
    public bool IsEdited { get => _isEdited; private set => Set(ref _isEdited, value); }

    internal void RefreshEdited() => IsEdited = Copies.Any(c => c.IsEdited);
}

/// <summary>One tunable, showing the value the selected archetypes share and writing to all of them.</summary>
public sealed class SoldierFieldRow(SoldierField field, AiSoldiersModel owner) : Observable
{
    private string _text = "";
    private string _vanilla = "";
    private bool _isChanged;

    public SoldierField Field { get; } = field;

    public string Group => Field.Group;

    public string Label => Field.Label;

    public string Help => Field.Help;

    /// <summary>The picks of a choice or yes/no field; null for a number.</summary>
    public string[]? Choices { get; } = field.Kind switch
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
        get => Choices is not null && int.TryParse(_text, out int i) && i >= 0 && i < Choices.Length ? Choices[i] : null;
        set
        {
            if (Choices is not null && value is not null)
            {
                Text = Array.IndexOf(Choices, value).ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public string Vanilla { get => _vanilla; private set => Set(ref _vanilla, value); }

    /// <summary>At least one selected archetype differs from the base game here.</summary>
    public bool IsChanged { get => _isChanged; private set => Set(ref _isChanged, value); }

    internal void Show(string text, string vanilla, bool changed)
    {
        _text = text;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Choice));
        Vanilla = vanilla;
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
    private Dictionary<uint, VfsFile> _containers = [];
    private string _filter = "";
    private bool _isLoaded;
    private bool _selecting;

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

    public string SelectionSummary => _archetypes.Count(a => a.IsSelected) switch
    {
        0 => "Tick one or more archetypes on the left.",
        1 => $"Editing {_archetypes.First(a => a.IsSelected).Label}.",
        int n => $"Editing {n} archetypes at once - a value shown blank differs between them; typing one sets it on all.",
    };

    public async Task LoadAsync(IProgress<string> progress)
    {
        List<string> worlds = [.. ArchetypeIndex.DiscoverWorlds(vm.AllKnownPaths).Where(w => w.StartsWith("world", StringComparison.OrdinalIgnoreCase))];
        List<SoldierCopy> copies = [];
        var containers = new Dictionary<uint, VfsFile>();

        foreach (string world in worlds)
        {
            progress.Report($"Reading {world}'s archetypes…");
            ArchetypeIndex index = await vm.ArchetypesOf(world, progress);
            foreach (IGrouping<uint, ArchetypeDefinition> library in index.Names
                         .Select(index.Winner).OfType<ArchetypeDefinition>()
                         .Where(d => SoldierFields.IsSoldier(d.Node))
                         .GroupBy(d => d.ContainerHash))
            {
                if (vm.FindByHash(library.Key) is { } container)
                {
                    containers[library.Key] = container;
                    copies.AddRange(await Task.Run(() => SoldierLibrary.Open(library, vm.Read(container), vm.ReadOriginal(container))));
                }
            }
        }

        _containers = containers;
        _archetypes =
        [
            .. copies
                .GroupBy(c => c.Definition.Name)
                .Select(g => new SoldierArchetype(g.Key, GroupOf(g.Key), [.. g]))
                .OrderBy(a => a.Group == "Enemies" ? 0 : 1)
                .ThenBy(a => a.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
        ];
        foreach (SoldierArchetype archetype in _archetypes)
        {
            archetype.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SoldierArchetype.IsSelected) && !_selecting)
                {
                    RefreshRows();
                }
            };
        }
        Rows = [.. SoldierFields.All.Select(f => new SoldierFieldRow(f, this))];
        OnPropertyChanged(nameof(Rows));
        ApplyFilter();
        RefreshRows();
        IsLoaded = true;
        OnPropertyChanged(nameof(IsDirty));
        progress.Report($"{_archetypes.Count} soldier archetypes across {string.Join(" and ", worlds)}");
    }

    /// <summary>Ticks every visible archetype, or clears them all.</summary>
    public void SelectAll(bool selected)
    {
        _selecting = true;
        foreach (SoldierArchetype archetype in selected ? Visible : _archetypes)
        {
            archetype.IsSelected = selected;
        }
        _selecting = false;
        RefreshRows();
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
            archetype.RefreshEdited();
        }
        if (wrote)
        {
            OnPropertyChanged(nameof(IsDirty));
        }
        return wrote;
    }

    internal void RefreshRow(SoldierFieldRow row)
    {
        var values = new HashSet<double?>();
        var vanilla = new HashSet<double?>();
        bool changed = false;
        foreach (SoldierCopy copy in _archetypes.Where(a => a.IsSelected).SelectMany(a => a.Copies))
        {
            double? value = row.Field.Read(copy.Entity);
            double? original = copy.Vanilla(row.Field);
            values.Add(value);
            vanilla.Add(original);
            changed |= value != original;
        }
        row.Show(
            values.Count == 1 ? Format(values.First()) : "",
            vanilla.Count switch { 0 => "", 1 => Format(vanilla.First()), _ => "varies" },
            changed);
    }

    /// <summary>Stages every edited archetype's fragment in each world; a copy back to vanilla is unstaged.</summary>
    public async Task<int> SaveAsync()
    {
        FcbClassDefinitions definitions = FcbDefinitionsProvider.Value.Value;
        List<SoldierArchetype> dirty = [.. _archetypes.Where(a => a.IsDirty)];
        var plans = await Task.Run(() => dirty
            .SelectMany(a => a.Copies)
            .GroupBy(c => c.Definition.ContainerHash)
            .Select(g => (Container: _containers[g.Key], Fragments: (IEnumerable<(string, string, bool)>)[.. g.Select(c => c.Plan(definitions))]))
            .ToList());

        vm.StageFragments(plans);
        foreach (SoldierArchetype archetype in dirty)
        {
            archetype.IsDirty = false;
        }
        OnPropertyChanged(nameof(IsDirty));
        return dirty.Count;
    }

    private void RefreshRows()
    {
        foreach (SoldierFieldRow row in Rows)
        {
            RefreshRow(row);
        }
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

    private static string GroupOf(string name) => name.Split('.')[0] switch
    {
        "enemy_archetypes" => "Enemies",
        "buddies" => "Buddies and civilians",
        string other => other,
    };

    private static string Format(double? value)
        => value is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";
}
