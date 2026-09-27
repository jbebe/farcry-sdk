using System.Collections.ObjectModel;
using System.Globalization;
using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.Ai;
using JackAll.Tools.World;

namespace JackAll.App.Ai;

/// <summary>One soldier archetype, with its copy in every single-player world that declares it.</summary>
public sealed class TunedArchetype(string name, string group, IReadOnlyList<TuningCopy> copies) : Observable
{
    private bool _isSelected;
    private bool _isEdited = copies.Any(c => c.IsEdited);

    public string Name { get; } = name;

    public string Label { get; } = name[(name.IndexOf('.') + 1)..];

    public string Group { get; } = group;

    public IReadOnlyList<TuningCopy> Copies { get; } = copies;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Changed since the last save.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Differs from the base game.</summary>
    public bool IsEdited { get => _isEdited; private set => Set(ref _isEdited, value); }

    internal void RefreshEdited() => IsEdited = Copies.Any(c => c.IsEdited);
}

/// <summary>One tunable, showing the value the selected archetypes share and writing to all of them.</summary>
public sealed class TuningFieldRow(TuningField field, AiArchetypesModel owner) : Observable
{
    private string _text = "";
    private string _vanilla = "";
    private bool _isChanged;

    public TuningField Field { get; } = field;

    public string Group => Field.Group;

    public string Label => Field.Label;

    public string Help => Field.Help;

    /// <summary>The picks of a choice or yes/no field; null for a number.</summary>
    public string[]? Choices { get; } = field.Kind switch
    {
        TuningFieldKind.Toggle => ["No", "Yes"],
        TuningFieldKind.Choice => field.Choices,
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
/// One tuning view of the AI tab - soldiers or weapons: the archetypes <paramref name="catalog"/> covers,
/// edited in each single-player world's winning declaration at once.
/// </summary>
public sealed class AiArchetypesModel(MainViewModel vm, TuningCatalog catalog, string listNote, string fieldNote) : Observable
{
    /// <summary>What the archetype list holds, shown above it.</summary>
    public string ListNote { get; } = listNote;

    /// <summary>How to read the fields, shown above them.</summary>
    public string FieldNote { get; } = fieldNote;

    private IReadOnlyList<TunedArchetype> _archetypes = [];
    private Dictionary<uint, VfsFile> _containers = [];
    private string _filter = "";
    private bool _isLoaded;
    private bool _selecting;

    public ObservableCollection<TunedArchetype> Visible { get; } = [];

    public IReadOnlyList<TuningFieldRow> Rows { get; private set; } = [];

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
        List<TuningCopy> copies = [];
        var containers = new Dictionary<uint, VfsFile>();

        foreach (string world in worlds)
        {
            progress.Report($"Reading {world}'s archetypes…");
            ArchetypeIndex index = await vm.ArchetypesOf(world, progress);
            foreach (IGrouping<uint, ArchetypeDefinition> library in index.Names
                         .Select(index.Winner).OfType<ArchetypeDefinition>()
                         .Where(d => catalog.Covers(d.Node))
                         .GroupBy(d => d.ContainerHash))
            {
                if (vm.FindByHash(library.Key) is { } container)
                {
                    containers[library.Key] = container;
                    copies.AddRange(await Task.Run(() => TuningLibrary.Open(catalog, library, vm.Read(container), vm.ReadOriginal(container))));
                }
            }
        }

        _containers = containers;
        _archetypes =
        [
            .. copies
                .GroupBy(c => c.Definition.Name)
                .Select(g => new TunedArchetype(g.Key, catalog.GroupOf(g.Key), [.. g]))
                .OrderBy(a => a.Group switch { "Enemies" => 0, "Multiplayer" => 2, _ => 1 })
                .ThenBy(a => a.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
        ];
        foreach (TunedArchetype archetype in _archetypes)
        {
            archetype.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TunedArchetype.IsSelected) && !_selecting)
                {
                    RefreshRows();
                }
            };
        }
        Rows = [.. catalog.Fields.Select(f => new TuningFieldRow(f, this))];
        OnPropertyChanged(nameof(Rows));
        ApplyFilter();
        RefreshRows();
        IsLoaded = true;
        OnPropertyChanged(nameof(IsDirty));
        progress.Report($"{_archetypes.Count} archetypes across {string.Join(" and ", worlds)}");
    }

    /// <summary>Ticks every visible archetype, or clears them all.</summary>
    public void SelectAll(bool selected)
    {
        _selecting = true;
        foreach (TunedArchetype archetype in selected ? Visible : _archetypes)
        {
            archetype.IsSelected = selected;
        }
        _selecting = false;
        RefreshRows();
    }

    internal bool TryWrite(TuningFieldRow row, string text)
    {
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return false;
        }

        bool wrote = false;
        foreach (TunedArchetype archetype in _archetypes.Where(a => a.IsSelected))
        {
            foreach (TuningCopy copy in archetype.Copies)
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

    internal void RefreshRow(TuningFieldRow row)
    {
        var values = new HashSet<double?>();
        var vanilla = new HashSet<double?>();
        bool changed = false;
        foreach (TuningCopy copy in _archetypes.Where(a => a.IsSelected).SelectMany(a => a.Copies))
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
        List<TunedArchetype> dirty = [.. _archetypes.Where(a => a.IsDirty)];
        var plans = await Task.Run(() => dirty
            .SelectMany(a => a.Copies)
            .GroupBy(c => c.Definition.ContainerHash)
            .Select(g => (Container: _containers[g.Key], Fragments: (IEnumerable<(string, string, bool)>)[.. g.Select(c => c.Plan(definitions))]))
            .ToList());

        vm.StageFragments(plans);
        foreach (TunedArchetype archetype in dirty)
        {
            archetype.IsDirty = false;
        }
        OnPropertyChanged(nameof(IsDirty));
        return dirty.Count;
    }

    private void RefreshRows()
    {
        foreach (TuningFieldRow row in Rows)
        {
            RefreshRow(row);
        }
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private void ApplyFilter()
    {
        string[] words = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Visible.Clear();
        foreach (TunedArchetype archetype in _archetypes.Where(a => words.All(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
        {
            Visible.Add(archetype);
        }
    }

    private static string Format(double? value)
        => value is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";
}
