using System.Globalization;

namespace JackAll.Core.Format.Move.Rules;

/// <summary>What is known about each value channel of a graph, by index.</summary>
public sealed class MoveChannels
{
    private readonly IReadOnlyList<MoveChannel> _channels;

    private MoveChannels(IReadOnlyList<MoveChannel> channels, IReadOnlyList<MoveChannel>? named)
    {
        _channels = channels;
        Named = named;
    }

    /// <summary>
    /// The named twin's table when there is one, otherwise the bare types the graph's own value
    /// container declares - an expansion has neither, and its channels stay unnamed.
    /// </summary>
    public static MoveChannels From(IReadOnlyList<MoveChannel>? named, MoveFile file)
    {
        if (named is { Count: > 0 })
        {
            return new MoveChannels(named, named);
        }

        List<MoveChannel> typed = [];
        MoveObject? container = file.Objects.FirstOrDefault(o => o.ClassName == "CMoveValueContainer");
        foreach (MoveOp op in container?.Ops ?? [])
        {
            if (op.Name == "m_eMVType")
            {
                typed.Add(new MoveChannel($"Channel {typed.Count}", null, (MoveValueType)op.Number));
            }
        }

        return new MoveChannels(typed, null);
    }

    /// <summary>The named twin's table, or null when the channels carry no names.</summary>
    public IReadOnlyList<MoveChannel>? Named { get; }

    public int Count => _channels.Count;

    public string NameOf(int channel)
        => channel < _channels.Count ? _channels[channel].Name : $"Channel {channel}";

    public MoveValueType TypeOf(int channel)
        => channel < _channels.Count ? _channels[channel].Type : MoveValueType.Unknown;

    public IReadOnlyList<string>? ValuesOf(int channel)
        => channel < _channels.Count ? _channels[channel].Values : null;

    /// <summary>A stored value as a person reads it: an enum by name, an angle in degrees.</summary>
    public string Format(int channel, double value) => TypeOf(channel) switch
    {
        MoveValueType.Enum when ValuesOf(channel) is { } names
                                && value >= 0 && value < names.Count && value == Math.Floor(value)
            => names[(int)value],
        MoveValueType.Bool => value != 0 ? "true" : "false",
        MoveValueType.Angle => ToText(channel, value) + "°",
        _ => ToText(channel, value),
    };

    /// <summary>A stored value as a number to edit; angles in degrees.</summary>
    public string ToText(int channel, double value)
        => (TypeOf(channel) == MoveValueType.Angle ? value * 180 / Math.PI : value)
            .ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The inverse of <see cref="ToText"/>.</summary>
    public bool TryParse(int channel, string text, out double value)
    {
        bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (ok && TypeOf(channel) == MoveValueType.Angle)
        {
            value = value * Math.PI / 180;
        }

        return ok;
    }
}
