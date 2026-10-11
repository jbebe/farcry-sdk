using JackAll.Tools.Spk;

namespace SoundBench.Dare;

/// <summary>What a block evaluates a sound tree at: the game parameters, which records are muted or soloed,
/// and how far the listener is.</summary>
/// <param name="Parameter">A game parameter's value, or null for one the playing object does not answer.</param>
public sealed record Listening(Func<uint, float?> Parameter, IReadOnlySet<uint> Muted, IReadOnlySet<uint> Soloed, float Distance);

/// <summary>One record of a playing event, as the dispatcher walked it: an event, a container, or a sample.</summary>
public sealed class SoundNode
{
    public required uint Id { get; init; }

    public required string Kind { get; init; }

    public string Detail { get; set; } = "";

    /// <summary>The bank the record came from.</summary>
    public string Bank { get; init; } = "";

    /// <summary>The resource's word [5], added to everything under it.</summary>
    public float GainDb { get; init; }

    /// <summary>The curves of the multilayer layer this node plays in.</summary>
    public IReadOnlyList<SpkCurve> Curves { get; init; } = [];

    public List<SoundNode> Children { get; } = [];

    public Voice? Voice { get; set; }

    public string? Problem { get; set; }

    /// <summary>The dB and pitch ratio down to this node, last evaluated.</summary>
    public float VolumeDb { get; private set; }

    public float Pitch { get; private set; } = 1;

    /// <summary>Each curve's parameter value and result, last evaluated.</summary>
    public float[] CurveInputs { get; private set; } = [];

    public float[] CurveOutputs { get; private set; } = [];

    public IEnumerable<SoundNode> Subtree() => Children.SelectMany(c => c.Subtree()).Prepend(this);

    /// <summary>Volume curves add their dB to the layer's, pitch curves multiply its ratio, down the tree.</summary>
    public void Evaluate(Listening at, float db = 0, float pitch = 1, bool audible = true, bool soloed = false)
    {
        if (CurveInputs.Length != Curves.Count)
        {
            CurveInputs = new float[Curves.Count];
            CurveOutputs = new float[Curves.Count];
        }
        for (int i = 0; i < Curves.Count; i++)
        {
            SpkCurve curve = Curves[i];
            float? x = at.Parameter(curve.Parameter);
            if (x is null)
            {
                Problem ??= $"reads parameter 0x{curve.Parameter:x8}, which a car does not answer";
            }
            CurveInputs[i] = x ?? 0;
            CurveOutputs[i] = curve.Evaluate(x ?? 0);
            if (curve.Target == 0)
            {
                db += CurveOutputs[i];
            }
            else if (curve.Target == 1)
            {
                pitch *= CurveOutputs[i];
            }
            else
            {
                Problem ??= $"has a curve of unknown target {curve.Target}";
            }
        }

        VolumeDb = db + GainDb;
        Pitch = pitch;
        audible &= !at.Muted.Contains(Id);
        soloed |= at.Soloed.Contains(Id);
        foreach (SoundNode child in Children)
        {
            child.Evaluate(at, VolumeDb, Pitch, audible, soloed);
        }
        Voice?.Update(VolumeDb, Pitch, audible && (at.Soloed.Count == 0 || soloed), at.Distance);
    }
}
