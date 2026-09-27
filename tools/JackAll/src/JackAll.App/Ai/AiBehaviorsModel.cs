using System.Globalization;
using JackAll.Core.Format;
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
    private string? _xml;
    private bool _isDirty;

    public IReadOnlyList<BehaviorRow> Rows { get; private set; } = [];

    public bool IsLoaded => _xml is not null;

    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public event Action? DirtyChanged;

    public void Load()
    {
        _xml = vm.ReadByPath(AdaptiveBehaviors.Path) is { } bytes ? AppText.DecodeUtf8(bytes) : null;
        byte[]? original = vm.FindByHash(NameHash.Compute(AdaptiveBehaviors.Path)) is { } file ? vm.ReadOriginal(file) : null;
        Dictionary<string, AdaptiveBehavior> vanilla = original is null
            ? []
            : AdaptiveBehaviors.Read(AppText.DecodeUtf8(original)).ToDictionary(b => b.Name);

        Rows = _xml is null
            ? []
            : [.. AdaptiveBehaviors.Read(_xml).Select(b => new BehaviorRow(b, vanilla.GetValueOrDefault(b.Name), MarkDirty))];
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(IsLoaded));
        IsDirty = false;
    }

    public void Save()
    {
        if (_xml is null || !IsDirty || vm.FindByHash(NameHash.Compute(AdaptiveBehaviors.Path)) is not { } file)
        {
            return;
        }
        _xml = AdaptiveBehaviors.Write(_xml, Rows.Select(r => new AdaptiveBehavior(r.Name, r.Chances)));
        vm.Replace(file, AppText.EncodeUtf8(_xml));
        IsDirty = false;
        DirtyChanged?.Invoke();
    }

    private void MarkDirty()
    {
        IsDirty = true;
        DirtyChanged?.Invoke();
    }
}
