using System.Globalization;
using JackAll.Core.Vfs;
using JackAll.Tools.Ai;

namespace JackAll.App.Ai;

/// <summary>One behaviour's chance at one progression level.</summary>
public sealed class BehaviorCell(BehaviorRow row, int level, double vanilla) : Observable
{
    public int Level { get; } = level;

    public double Vanilla { get; } = vanilla;

    public double Value
    {
        get => row.Chances[Level];
        set
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (clamped != row.Chances[Level])
            {
                row.Chances[Level] = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChanged));
                row.Changed();
            }
        }
    }

    public bool IsChanged => Value != Vanilla;

    public string Tip => $"Level {Level}: {Value.ToString("0.#", CultureInfo.InvariantCulture)}% (base game {Vanilla.ToString("0.#", CultureInfo.InvariantCulture)}%)";
}

/// <summary>One adaptive behaviour across the 28 levels.</summary>
public sealed class BehaviorRow : Observable
{
    private readonly Action _changed;

    public BehaviorRow(AdaptiveBehavior behavior, AdaptiveBehavior? vanilla, Action changed)
    {
        Name = behavior.Name;
        Chances = behavior.Chances;
        _changed = changed;
        Cells = [.. Enumerable.Range(0, AdaptiveBehaviors.Levels)
            .Select(i => new BehaviorCell(this, i, vanilla?.Chances[i] ?? behavior.Chances[i]))];
    }

    public string Name { get; }

    public string Description => AdaptiveBehaviors.Descriptions.GetValueOrDefault(Name, "No description yet.");

    public double[] Chances { get; }

    public BehaviorCell[] Cells { get; }

    /// <summary>Sets every level from <paramref name="from"/> on to <paramref name="value"/>.</summary>
    public void Fill(int from, double value)
    {
        foreach (BehaviorCell cell in Cells.Skip(from))
        {
            cell.Value = value;
        }
    }

    internal void Changed() => _changed();
}

/// <summary>The Behaviour odds view of the AI tab: the adaptive-behaviour table of gamemodesconfig.xml.</summary>
public sealed class AiBehaviorsModel(MainViewModel vm) : Observable
{
    private VfsFile? _file;
    private string? _xml;
    private bool _isDirty;

    public IReadOnlyList<BehaviorRow> Rows { get; private set; } = [];

    public bool IsLoaded => _xml is not null;

    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public void Load()
    {
        _file = vm.FindByPath(AdaptiveBehaviors.Path);
        _xml = _file is null ? null : AppText.DecodeUtf8(vm.Read(_file));
        Dictionary<string, AdaptiveBehavior> vanilla = _file is not null && vm.ReadOriginal(_file) is { } original
            ? AdaptiveBehaviors.Read(AppText.DecodeUtf8(original)).ToDictionary(b => b.Name)
            : [];

        Rows = _xml is null
            ? []
            : [.. AdaptiveBehaviors.Read(_xml).Select(b => new BehaviorRow(b, vanilla.GetValueOrDefault(b.Name), () => IsDirty = true))];
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(IsLoaded));
        IsDirty = false;
    }

    public void Save()
    {
        if (_file is null || _xml is null || !IsDirty)
        {
            return;
        }
        _xml = AdaptiveBehaviors.Write(_xml, Rows.Select(r => new AdaptiveBehavior(r.Name, r.Chances)));
        vm.Replace(_file, AppText.EncodeUtf8(_xml));
        IsDirty = false;
    }
}
