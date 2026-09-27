using System.Collections.ObjectModel;
using System.Globalization;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Ai;

namespace JackAll.App.Ai;

/// <summary>One knot of the selected curve; editing it moves that knot in every copy.</summary>
public sealed class CurvePointRow(int index, CurvePoint point, CurvePoint? vanilla, AiCurvesModel owner) : Observable
{
    public int Index { get; } = index;

    public string X
    {
        get => Format(point.X);
        set => owner.TryWrite(Index, value, null);
    }

    public string Y
    {
        get => Format(point.Y);
        set => owner.TryWrite(Index, null, value);
    }

    public string Vanilla => vanilla is { } v ? $"{Format(v.X)} → {Format(v.Y)}" : "";

    public bool IsChanged => vanilla is { } v && (v.X != point.X || v.Y != point.Y);

    private static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// The Curves view of the AI tab: the shooting-system and AI-weapon curves the weapon properties name,
/// edited knot by knot in every single-player world's copy.
/// </summary>
public sealed class AiCurvesModel(MainViewModel vm) : Observable
{
    private static readonly string[] Prefixes = ["Curves.ShootingSystem.", "Curves.AIWeapon."];

    private AiLibrarySet _set = AiLibrarySet.Empty;
    private IReadOnlyList<TunedArchetype> _curves = [];
    private TunedArchetype? _selected;
    private string _filter = "";
    private bool _isLoaded;

    public ObservableCollection<TunedArchetype> Visible { get; } = [];

    public IReadOnlyList<CurvePointRow> Rows { get; private set; } = [];

    /// <summary>The selected curve's knots, and the base game's, for the chart.</summary>
    public (IReadOnlyList<CurvePoint> Current, IReadOnlyList<CurvePoint> Vanilla) Shape { get; private set; } = ([], []);

    public bool IsLoaded { get => _isLoaded; private set => Set(ref _isLoaded, value); }

    public bool IsDirty => _curves.Any(c => c.IsDirty);

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

    public TunedArchetype? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Refresh();
            }
        }
    }

    public async Task LoadAsync(IProgress<string> progress)
    {
        Selected = null;
        _set = await AiLibrarySet.LoadAsync(vm, IsTunedCurve, progress);
        _curves =
        [
            .. _set.Copies
                .GroupBy(c => c.Definition.Name)
                .Select(g => new TunedArchetype(g.Key, GroupOf(g.Key), [.. g],
                    c => !AiCurve.Read(c.Entity).SequenceEqual(AiCurve.Read(c.VanillaEntity))))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
        ];
        ApplyFilter();
        IsLoaded = true;
        progress.Report($"{_curves.Count} curves across {string.Join(" and ", _set.Worlds)}");
    }

    internal void TryWrite(int index, string? x, string? y)
    {
        if (_selected is not { } curve || !Parse(x, out float? newX) || !Parse(y, out float? newY))
        {
            Refresh();
            return;
        }
        foreach (TuningCopy copy in curve.Copies)
        {
            CurvePoint point = AiCurve.Read(copy.Entity)[index];
            AiCurve.Write(copy.Entity, index, newX ?? point.X, newY ?? point.Y);
        }
        curve.IsDirty = true;
        curve.RefreshEdited();
        OnPropertyChanged(nameof(IsDirty));
        Refresh();
    }

    public async Task<int> SaveAsync()
    {
        List<TunedArchetype> dirty = [.. _curves.Where(c => c.IsDirty)];
        await _set.StageAsync(vm, dirty.SelectMany(c => c.Copies));
        foreach (TunedArchetype curve in dirty)
        {
            curve.IsDirty = false;
        }
        OnPropertyChanged(nameof(IsDirty));
        return dirty.Count;
    }

    private void Refresh()
    {
        TuningCopy? copy = _selected?.Copies[0];
        IReadOnlyList<CurvePoint> points = copy is null ? [] : AiCurve.Read(copy.Entity);
        IReadOnlyList<CurvePoint> vanilla = copy is null ? [] : AiCurve.Read(copy.VanillaEntity);
        Rows = [.. points.Select((p, i) => new CurvePointRow(i, p, i < vanilla.Count ? vanilla[i] : null, this))];
        Shape = (points, vanilla);
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Shape));
    }

    private void ApplyFilter()
    {
        string[] words = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Visible.Clear();
        foreach (TunedArchetype curve in _curves.Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
        {
            Visible.Add(curve);
        }
    }

    private static bool IsTunedCurve(FcbObject entity)
    {
        string name = FcbEntityFields.ReadString(entity, WorldHashes.HidName);
        return Prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) && AiCurve.IsCurve(entity);
    }

    private static string GroupOf(string name) => name.StartsWith(Prefixes[0], StringComparison.Ordinal) ? "Shooting system" : "AI weapon";

    private static bool Parse(string? text, out float? value)
    {
        value = null;
        if (text is null)
        {
            return true;
        }
        if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }
}
