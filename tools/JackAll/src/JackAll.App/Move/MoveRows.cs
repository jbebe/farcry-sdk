using System.Globalization;
using JackAll.Core.Format.Move;
using JackAll.Core.Format.Move.Rules;

namespace JackAll.App.Move;

/// <summary>A choice in a combo box: a channel, a value, or "any" when <see cref="Value"/> is null.</summary>
public sealed record MoveOption(double? Value, string Label)
{
    public override string ToString() => Label;

    /// <summary>A channel's values to pick from, or null when it holds a number to type.</summary>
    public static IReadOnlyList<MoveOption>? ValuesOf(MoveChannels channels, int channel)
        => channels.TypeOf(channel) switch
        {
            MoveValueType.Bool => [new MoveOption(1, "true"), new MoveOption(0, "false")],
            MoveValueType.Enum when channels.ValuesOf(channel) is { } names
                => [.. names.Select((n, i) => new MoveOption(i, n))],
            _ => null,
        };
}

/// <summary>One state in the situation list.</summary>
public sealed record MoveStateRow(MoveObject State, uint Hash, string Name, string Group)
{
    public override string ToString() => Name;
}

/// <summary>One channel the picked situation tests, and what the user says it currently holds.</summary>
public sealed class MoveSituationRow(int channel, string name, IReadOnlyList<MoveOption>? options) : Observable
{
    private MoveOption? _selected = options?[0];
    private string _text = string.Empty;

    public int Channel { get; } = channel;

    public string Name { get; } = name;

    /// <summary>"any" and the channel's values, or null for a number typed as text.</summary>
    public IReadOnlyList<MoveOption>? Options { get; } = options;

    public bool IsChoice => Options is not null;

    public bool IsText => !IsChoice;

    public MoveOption? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }
}

/// <summary>One rule as the grid shows it.</summary>
public sealed class MoveRuleRow(MoveRule rule, string weapon, string when, string plays, string blend, string sharedWith)
    : Observable
{
    private MoveVerdict _verdict = MoveVerdict.No;

    public MoveRule Rule { get; } = rule;

    public int Number => Rule.Order + 1;

    public string Weapon { get; } = weapon;

    public string When { get; } = when;

    public string Plays { get; } = plays;

    public string Blend { get; } = blend;

    public string SharedWith { get; } = sharedWith;

    public MoveVerdict Verdict
    {
        get => _verdict;
        set
        {
            if (Set(ref _verdict, value))
            {
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(IsQuiet));
            }
        }
    }

    public string Glyph => Verdict switch
    {
        MoveVerdict.Plays => "▶",
        MoveVerdict.MayPlay => "?",
        MoveVerdict.Shadowed => "–",
        _ => string.Empty,
    };

    /// <summary>Greyed: this rule does not play in the situation as set.</summary>
    public bool IsQuiet => Verdict is MoveVerdict.No or MoveVerdict.Shadowed;
}

/// <summary>One condition gating the selected rule, and where it lives.</summary>
public sealed record MoveConditionRow(MoveObject Owner, MoveCondition Condition, string Text, string Source);

/// <summary>One clip the selected rule can play.</summary>
public sealed record MoveClipRow(MoveClipSite Site, string Role, string Path)
{
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>The clip editor under the rule detail: a path, and the clip's timing.</summary>
/// <param name="library">Every <c>.mab</c> path the game ships, sorted case-insensitively.</param>
public sealed class MoveClipForm(string[] library) : Observable
{
    private MoveClipRow? _clip;
    private string _path = string.Empty;
    private string _blend = string.Empty;
    private string _speed = string.Empty;
    private string _start = string.Empty;
    private string _stop = string.Empty;
    private bool _interruptible;

    public MoveClipRow? Clip
    {
        get => _clip;
        set
        {
            if (!Set(ref _clip, value))
            {
                return;
            }

            MoveObject? owner = value?.Site.Owner;
            Path = value?.Path ?? string.Empty;
            Blend = Format(owner?.FieldF32("m_flBlendTime"));
            Speed = Format(owner?.FieldF32("m_flMultiplier"));
            Start = Format(owner?.FieldF32("m_flStartTime"));
            Stop = Format(owner?.FieldF32("m_flStopTime"));
            Interruptible = owner?.Field("m_fInterruptible") is > 0;
            OnPropertyChanged(nameof(HasClip));
            OnPropertyChanged(nameof(HasTiming));
        }
    }

    public bool HasClip => Clip is not null;

    public bool HasTiming => Clip?.Site.Owner.FieldF32("m_flBlendTime") is not null;

    public string Path { get => _path; set => Set(ref _path, value); }

    public string Blend { get => _blend; set => Set(ref _blend, value); }

    public string Speed { get => _speed; set => Set(ref _speed, value); }

    public string Start { get => _start; set => Set(ref _start, value); }

    public string Stop { get => _stop; set => Set(ref _stop, value); }

    public bool Interruptible { get => _interruptible; set => Set(ref _interruptible, value); }

    public bool IsShipped(string path) => Array.BinarySearch(library, path, StringComparer.OrdinalIgnoreCase) >= 0;

    private static string Format(float? value)
        => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>An operator as the condition editor offers it.</summary>
public sealed record MoveOpOption(MoveConditionOp Op, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The condition editor under the rule detail: channel, operator and value.</summary>
public sealed class MoveConditionForm : Observable
{
    private static readonly MoveOpOption Is = new(MoveConditionOp.Is, "is");
    private static readonly MoveOpOption IsNot = new(MoveConditionOp.IsNot, "is not");
    private static readonly MoveOpOption Between = new(MoveConditionOp.Between, "between");

    private readonly MoveChannels _channels;
    private MoveOption? _channel;
    private IReadOnlyList<MoveOpOption> _ops = [];
    private MoveOpOption _op = Is;
    private IReadOnlyList<MoveOption>? _values;
    private MoveOption? _value;
    private string _low = string.Empty;
    private string _high = string.Empty;
    private bool _isOr;

    public MoveConditionForm(MoveChannels channels)
    {
        _channels = channels;
        Channels =
        [
            .. Enumerable.Range(0, channels.Count)
                .Where(i => channels.TypeOf(i) is not (MoveValueType.Unknown or MoveValueType.EntityId))
                .Select(i => new MoveOption(i, channels.NameOf(i)))
                .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public IReadOnlyList<MoveOption> Channels { get; }

    public MoveOption? Channel
    {
        get => _channel;
        set
        {
            if (!Set(ref _channel, value) || value?.Value is not { } index)
            {
                return;
            }

            int channel = (int)index;
            Ops = _channels.TypeOf(channel) switch
            {
                MoveValueType.Enum or MoveValueType.UInt8 => [Is, IsNot],
                MoveValueType.Bool => [Is],
                MoveValueType.Int => [Is, IsNot, Between],
                _ => [Between],
            };
            Op = Ops[0];
            Values = MoveOption.ValuesOf(_channels, channel);
            Value = Values?[0];
        }
    }

    public IReadOnlyList<MoveOpOption> Ops
    {
        get => _ops;
        private set => Set(ref _ops, value);
    }

    public MoveOpOption Op
    {
        get => _op;
        set
        {
            if (value is not null && Set(ref _op, value))
            {
                OnPropertyChanged(nameof(IsRange));
                OnPropertyChanged(nameof(IsChoice));
                OnPropertyChanged(nameof(IsTyped));
            }
        }
    }

    public IReadOnlyList<MoveOption>? Values
    {
        get => _values;
        private set
        {
            if (Set(ref _values, value))
            {
                OnPropertyChanged(nameof(IsChoice));
                OnPropertyChanged(nameof(IsTyped));
            }
        }
    }

    public MoveOption? Value { get => _value; set => Set(ref _value, value); }

    public bool IsRange => Op.Op == MoveConditionOp.Between;

    public bool IsChoice => !IsRange && Values is not null;

    public bool IsTyped => !IsChoice;

    /// <summary>The value, or a range's lower bound; angles in degrees.</summary>
    public string Low { get => _low; set => Set(ref _low, value); }

    public string High { get => _high; set => Set(ref _high, value); }

    public bool IsOr { get => _isOr; set => Set(ref _isOr, value); }

    public void Load(MoveCondition condition)
    {
        MoveConditionSpec spec = condition.Spec;
        Channel = Channels.FirstOrDefault(c => c.Value == spec.Channel);
        Op = Ops.FirstOrDefault(o => o.Op == spec.Op) ?? Op;
        Value = Values?.FirstOrDefault(v => v.Value == spec.Value);
        Low = _channels.ToText(spec.Channel, spec.Value);
        High = _channels.ToText(spec.Channel, spec.High);
        IsOr = spec.IsOr;
    }

    /// <summary>What the form says, or null with <paramref name="error"/> set when it does not parse.</summary>
    public MoveConditionSpec? ToSpec(out string? error)
    {
        error = null;
        if (Channel?.Value is not { } index)
        {
            error = "Pick a channel to test.";
            return null;
        }

        int channel = (int)index;
        if (IsChoice)
        {
            if (Value?.Value is not { } choice)
            {
                error = "Pick a value.";
                return null;
            }

            return new MoveConditionSpec(channel, Op.Op, choice, 0, IsOr);
        }

        double high = 0;
        if (!_channels.TryParse(channel, Low, out double low) || (IsRange && !_channels.TryParse(channel, High, out high)))
        {
            error = IsRange ? "Both ends of the range have to be numbers." : "The value has to be a number.";
            return null;
        }

        return new MoveConditionSpec(channel, Op.Op, low, high, IsOr);
    }
}
