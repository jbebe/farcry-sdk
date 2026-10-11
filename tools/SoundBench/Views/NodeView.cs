using JackAll.App;
using SoundBench.Dare;

namespace SoundBench.Views;

/// <summary>A row of the voice tree: one node of a playing event, its numbers copied out under the bench's lock
/// and formatted after it.</summary>
public sealed class NodeView : Observable
{
    private readonly Bench _bench;
    private readonly string[] _curveNames;
    private readonly float[] _inputs, _outputs;
    private float _volumeDb, _pitch, _heardDb, _peakDb;
    private bool _virtual, _done;
    private string? _problem;
    private string _values = "", _curves = "", _problemText = "";
    private double _meter;
    private bool _muted, _soloed;

    public NodeView(Bench bench, SoundNode node, Func<uint, string> parameterName, string? field = null)
    {
        _bench = bench;
        Node = node;
        Title = $"{(field is null ? "" : field + ": ")}{node.Kind} 0x{node.Id:x8}";
        Detail = string.Join(" · ", new[] { node.Detail, node.Bank }.Where(s => s.Length > 0));
        _curveNames = [.. node.Curves.Select(c => parameterName(c.Parameter))];
        _inputs = new float[node.Curves.Count];
        _outputs = new float[node.Curves.Count];
        _muted = bench.Muted.Contains(node.Id);
        _soloed = bench.Soloed.Contains(node.Id);
        Children = [.. node.Children.Select(c => new NodeView(bench, c, parameterName))];
        Subtree = [this, .. Children.SelectMany(c => c.Subtree)];
    }

    public SoundNode Node { get; }
    public string Title { get; }
    public string Detail { get; }
    public IReadOnlyList<NodeView> Children { get; }
    public IReadOnlyList<NodeView> Subtree { get; }
    public bool IsVoice => Node.Voice is not null;

    /// <summary>An event that plays under the engine's, listed after them.</summary>
    public bool Background { get; init; }

    public string Values { get => _values; private set => Set(ref _values, value); }
    public string Curves { get => _curves; private set => Set(ref _curves, value); }
    public string Problem { get => _problemText; private set => Set(ref _problemText, value); }

    /// <summary>The voice's last block peak on a 60 dB scale, 0..1.</summary>
    public double Meter { get => _meter; private set => Set(ref _meter, value); }

    public bool Muted
    {
        get => _muted;
        set => Toggle(ref _muted, value, _bench.Muted);
    }

    public bool Soloed
    {
        get => _soloed;
        set => Toggle(ref _soloed, value, _bench.Soloed);
    }

    /// <summary>Copies the node's numbers out; call under <see cref="Bench.Sync"/>.</summary>
    public void Capture()
    {
        _volumeDb = Node.VolumeDb;
        _pitch = Node.Pitch;
        _problem = Node.Problem;
        Node.CurveInputs.AsSpan(0, Math.Min(_inputs.Length, Node.CurveInputs.Length)).CopyTo(_inputs);
        Node.CurveOutputs.AsSpan(0, Math.Min(_outputs.Length, Node.CurveOutputs.Length)).CopyTo(_outputs);
        if (Node.Voice is { } voice)
        {
            _heardDb = voice.HeardDb;
            _peakDb = voice.PeakDb;
            _virtual = voice.Virtual;
            _done = voice.Done;
        }
    }

    /// <summary>Formats what <see cref="Capture"/> copied, raising only what changed.</summary>
    public void Publish()
    {
        Problem = _problem ?? "";
        Curves = string.Join("   ", Node.Curves.Select((c, i) =>
            $"{_curveNames[i]} {_inputs[i]:0.##} → " + (c.Target == 0 ? $"{_outputs[i]:0.0} dB" : $"×{_outputs[i]:0.000}")));

        string gain = Node.GainDb != 0 ? $"gain {Node.GainDb:0.#} dB, " : "";
        if (IsVoice)
        {
            Values = $"{gain}{Math.Clamp(_volumeDb, -96, 0):0.0} dB{(_volumeDb > 0 ? " (clamped at 0)" : "")}, heard {_heardDb:0.0} dB, " +
                     $"pitch ×{_pitch:0.000}{(_virtual ? $", virtual: under {Voice.AudibleFloorDb} dB" : "")}{(_done ? ", ended" : "")}";
            Meter = Math.Clamp((_peakDb + 60) / 60, 0, 1);
        }
        else
        {
            bool changed = _volumeDb != 0 || _pitch != 1;
            Values = Node.Children.Count > 0 && changed ? $"{gain}{_volumeDb:0.0} dB, ×{_pitch:0.000}" : gain.TrimEnd(',', ' ');
        }
    }

    private void Toggle(ref bool field, bool on, HashSet<uint> set, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, on, name))
        {
            return;
        }
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
}
