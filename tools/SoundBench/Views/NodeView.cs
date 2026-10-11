using JackAll.Tools.Spk;
using SoundBench.Dare;

namespace SoundBench.Views;

/// <summary>A row of the voice tree: one node of a playing event, its values copied out each refresh.</summary>
public sealed class NodeView : Observable
{
    private readonly Bench _bench;
    private readonly Func<uint, string> _parameterName;

    public NodeView(Bench bench, SoundNode node, Func<uint, string> parameterName, string? field = null)
    {
        _bench = bench;
        _parameterName = parameterName;
        Node = node;
        Field = field;
        Title = $"{(field is null ? "" : field + ": ")}{node.Kind} 0x{node.Id:x8}";
        Children = [.. node.Children.Select(c => new NodeView(bench, c, parameterName))];
    }

    public SoundNode Node { get; }

    /// <summary>The field that played the event, on an event's own row.</summary>
    public string? Field { get; }
    public string Title { get; }
    public IReadOnlyList<NodeView> Children { get; }
    public bool IsVoice => Node.Voice is not null;

    public string Detail { get; private set; } = "";
    public string Values { get; private set; } = "";
    public string Curves { get; private set; } = "";
    public string? Problem { get; private set; }
    public bool HasProblem => Problem is not null;

    /// <summary>The voice's last block peak on a 60 dB scale, 0..1.</summary>
    public double Meter { get; private set; }

    public bool Muted
    {
        get => Has(_bench.Muted);
        set => Set(_bench.Muted, value);
    }

    public bool Soloed
    {
        get => Has(_bench.Soloed);
        set => Set(_bench.Soloed, value);
    }

    public IEnumerable<NodeView> Subtree() => Children.SelectMany(c => c.Subtree()).Prepend(this);

    /// <summary>Copies the node's values out; call under <see cref="Bench.Sync"/>, then <see cref="Publish"/>.</summary>
    public void Refresh()
    {
        Detail = string.Join(" · ", new[] { Node.Detail, Node.Bank }.Where(s => s.Length > 0));
        Problem = Node.Problem;
        Curves = string.Join("   ", Node.Curves.Select((c, i) =>
            $"{_parameterName(c.Parameter)} {Node.CurveInputs.ElementAtOrDefault(i):0.##} → " +
            (c.Target == 0 ? $"{Node.CurveOutputs.ElementAtOrDefault(i):0.0} dB" : $"×{Node.CurveOutputs.ElementAtOrDefault(i):0.000}")));

        string gain = Node.GainDb != 0 ? $"gain {Node.GainDb:0.#} dB, " : "";
        if (Node.Voice is { } voice)
        {
            Values = $"{gain}{voice.LevelDb:0.0} dB{(voice.Clamped ? " (clamped at 0)" : "")}, heard {voice.HeardDb:0.0} dB, " +
                     $"pitch ×{voice.Pitch:0.000}{(voice.Virtual ? ", virtual: under -48 dB" : "")}{(voice.Done ? ", ended" : "")}";
            Meter = Math.Clamp((voice.PeakDb + 60) / 60, 0, 1);
        }
        else
        {
            bool changed = Node.VolumeDb != 0 || Node.Pitch != 1;
            Values = Node.Children.Count > 0 && changed ? $"{gain}{Node.VolumeDb:0.0} dB, ×{Node.Pitch:0.000}" : gain.TrimEnd(',', ' ');
        }
    }

    public void Publish() => NotifyAll();

    private bool Has(HashSet<uint> set)
    {
        lock (_bench.Sync)
        {
            return set.Contains(Node.Id);
        }
    }

    private void Set(HashSet<uint> set, bool on)
    {
        lock (_bench.Sync)
        {
            if (on)
            {
                set.Add(Node.Id);
            }
            else
            {
                set.Remove(Node.Id);
            }
        }
    }

    /// <summary>A curve as the plot draws it, the parameter's current value marked on it.</summary>
    public static IReadOnlyList<JackAll.App.CurveSeries> Plot(SpkCurve curve, float input, string brush)
    {
        System.Windows.Point[] points = [.. curve.Points.Select(p => new System.Windows.Point(p.X, p.Y))];
        double low = curve.Points.Min(p => p.Y), high = curve.Points.Max(p => p.Y);
        double value = curve.Evaluate(input);
        double tick = Math.Max(1e-3, (curve.Points.Max(p => p.X) - curve.Points.Min(p => p.X)) * 0.015);
        return
        [
            new(points, brush),
            new([new(input, low), new(input, high)], "CursorBrush", 1, Dashed: true),
            new([new(input - tick, value), new(input + tick, value)], "CursorBrush", 4),
        ];
    }
}
