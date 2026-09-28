namespace JackAll.Tools.Spk;

/// <summary>How a named word is written as an XML attribute.</summary>
public enum SpkWordFormat
{
    /// <summary>Another record's id, as <c>0x0044930e</c>.</summary>
    Id,

    /// <summary>Q16.16 fixed point, as a decimal.</summary>
    Q16,

    Bool,

    /// <summary>A random container's Q16.16 weight, written together with its entries' weights.</summary>
    Weight,
}

/// <summary>A sub-header word the XML names instead of writing it as <c>wN</c>; <see cref="Refers"/>
/// is set when the word is another record's id.</summary>
public sealed record SpkWordField(string Name, int Index, SpkWordFormat Format, SpkReference? Refers = null);

/// <summary>
/// The shape of a composite's tail entries: <see cref="Stride"/> bytes each, the child id first, the
/// value and extra words at these byte offsets (-1 for none), everything else zero.
/// </summary>
public sealed record SpkChildren(int Stride, int ValueAt, int ExtraAt, string Element, string RefAttribute, SpkReference Refers);

/// <summary>
/// What each event type and resource kind means word by word: which words the XML names, which the
/// writer derives from the rest of the bank, and what an unwritten word defaults to.
/// </summary>
/// <remarks>Meanings are from docs/docs/file-formats/spk.md. A word in none of these lists is kept
/// as-is and written as <c>wN</c> when it differs from its default.</remarks>
public sealed class SpkLayout
{
    public const int EventWords = 17;
    public const int ResourceWords = 32;

    public const uint NoId = 0xFFFFFFFF;

    /// <summary>Q16.16 one: a gain of 1.0, or a random container's whole weight.</summary>
    public const uint One = 0x10000;

    /// <summary>Resource words a sample's audio decides, filled from it on write.</summary>
    public const int SampleByteLength = 2, SampleChannels = 17, SampleRate = 19, SampleByteRate = 20,
        SampleOneShotFrames = 21, SampleOneShotBytes = 22, SampleLoopFrames = 23, SampleLoopBytes = 24,
        SampleCodec = 25;

    public const int SampleGain = 5, SampleAudio = 7, SampleLoop = 13;

    /// <summary>A Play's rolloff curve, and whether it plays at a position. Retail sets <c>positioned</c> on
    /// every event with a rolloff; one that is set fails to play through a first-person sound type.</summary>
    public const int PlayRolloff = 7, PlayPositioned = 14;

    private static readonly SpkWordField Gain = new("gainDb", SampleGain, SpkWordFormat.Q16);

    public static readonly SpkLayout Play = new("Play", (uint)SpkEventType.Leaf,
        [new("sound", 2, SpkWordFormat.Id, SpkReference.Resource),
            new("rolloff", PlayRolloff, SpkWordFormat.Id, SpkReference.Rolloff),
            new("positioned", PlayPositioned, SpkWordFormat.Bool)],
        defaults: new() { [4] = One, [PlayRolloff] = NoId });

    public static readonly SpkLayout StopNGo = new("StopNGo", (uint)SpkEventType.StopNGo,
        [new("stop", 2, SpkWordFormat.Id, SpkReference.Event), new("play", 3, SpkWordFormat.Id, SpkReference.Event)]);

    public static readonly SpkLayout SetReverb = new("SetReverb", (uint)SpkEventType.SetReverb,
        [new("effect", 2, SpkWordFormat.Id)]);

    public static readonly SpkLayout SwitchEvent = new("SwitchEvent", (uint)SpkEventType.Switch,
        [new("group", 3, SpkWordFormat.Id), new("default", 4, SpkWordFormat.Id, SpkReference.Event)],
        derived: [5], childCount: 6, children: new(12, 8, -1, "Case", "event", SpkReference.Event));

    public static readonly SpkLayout MultiEvent = new("MultiEvent", (uint)SpkEventType.List, [],
        derived: [2], childCount: 3, children: new(4, -1, -1, "Child", "event", SpkReference.Event));

    /// <summary>Any other event type; which of them <c>target</c> points at is <see cref="TargetOf"/>.</summary>
    public static readonly SpkLayout OtherEvent = new("Event", null, [new("target", 2, SpkWordFormat.Id)]);

    public static readonly SpkLayout Sample = new("Sample", (uint)SpkResourceKind.Sample,
        [Gain, new("audio", SampleAudio, SpkWordFormat.Id, SpkReference.Audio), new("loop", SampleLoop, SpkWordFormat.Bool)],
        derived: [SampleByteLength, SampleChannels, SampleRate, SampleByteRate, SampleOneShotFrames,
            SampleOneShotBytes, SampleLoopFrames, SampleLoopBytes, SampleCodec],
        defaults: new() { [9] = 1, [12] = 1, [28] = 7, [30] = 1, [31] = NoId });

    public static readonly SpkLayout Switch = new("Switch", (uint)SpkResourceKind.Switch,
        [Gain, new("group", 8, SpkWordFormat.Id), new("default", 9, SpkWordFormat.Id, SpkReference.Resource)],
        childCount: 7, children: new(8, 4, -1, "Case", "resource", SpkReference.Resource));

    /// <summary>Word [8] is the chance of playing nothing: with it, the entry weights sum to 1.0.
    /// <c>repeatSilence</c> allows two silent plays in a row; <c>sequence</c> steps through the entries
    /// per emitter from a random start, ignoring weights and silence. Word [3] is the entries' offset.</summary>
    public static readonly SpkLayout Random = new("Random", (uint)SpkResourceKind.Random,
        [Gain, new("silence", 8, SpkWordFormat.Weight), new("repeatSilence", 9, SpkWordFormat.Bool),
            new("sequence", 10, SpkWordFormat.Bool)],
        derived: [3], childCount: 7, children: new(16, 4, 8, "Choice", "resource", SpkReference.Resource));

    /// <summary>Its layers are written by hand: each nests curves of points.</summary>
    public static readonly SpkLayout Multilayer = new("Multilayer", (uint)SpkResourceKind.Multilayer, [Gain], childCount: 7);

    public static readonly SpkLayout OtherResource = new("Resource", null, [Gain]);

    private static readonly SpkLayout[] Events = [Play, StopNGo, SetReverb, SwitchEvent, MultiEvent];
    private static readonly SpkLayout[] Resources = [Sample, Switch, Random, Multilayer];

    public static readonly IReadOnlyList<SpkLayout> All = [.. Events, OtherEvent, .. Resources, OtherResource];

    private readonly HashSet<int> _named;

    private SpkLayout(string element, uint? kind, SpkWordField[] fields, int[]? derived = null,
        Dictionary<int, uint>? defaults = null, int? childCount = null, SpkChildren? children = null)
    {
        Element = element;
        Kind = kind;
        Fields = fields;
        Derived = [0, 1, .. derived ?? [], .. childCount is { } count ? [count] : Array.Empty<int>()];
        Defaults = defaults ?? [];
        ChildCount = childCount;
        Children = children;
        _named = [.. fields.Select(f => f.Index), .. Derived];
    }

    public string Element { get; }

    /// <summary>The event type or resource kind, or null for the catch-alls, which take it from XML.</summary>
    public uint? Kind { get; }

    public SpkWordField[] Fields { get; }

    /// <summary>Words the writer computes: own id, kind, child count and offsets, a sample's audio words.</summary>
    public int[] Derived { get; }

    public IReadOnlyDictionary<int, uint> Defaults { get; }

    /// <summary>The word holding the child count, for a composite.</summary>
    public int? ChildCount { get; }

    public SpkChildren? Children { get; }

    public bool IsEvent => Events.Contains(this) || this == OtherEvent;

    public int WordCount => IsEvent ? EventWords : ResourceWords;

    /// <summary>The XML attribute naming the kind, for the catch-alls.</summary>
    public string? KindAttribute => Kind is not null ? null : IsEvent ? "type" : "kind";

    /// <summary>Whether a word is named or derived, so never written as a plain <c>wN</c>.</summary>
    public bool IsNamed(int index) => _named.Contains(index);

    public uint Default(int index) => Defaults.GetValueOrDefault(index);

    /// <summary>A fresh sub-header: every word at its default.</summary>
    public uint[] NewWords()
    {
        uint[] words = new uint[WordCount];
        foreach ((int index, uint value) in Defaults)
        {
            words[index] = value;
        }
        return words;
    }

    /// <summary>What an <see cref="OtherEvent"/>'s <c>target</c> points at, by its event type.</summary>
    public static SpkReference? TargetOf(uint eventType) => eventType switch
    {
        (uint)SpkEventType.NoOpLinked => SpkReference.Event,
        5 or 6 or 7 or 9 => SpkReference.Resource,
        _ => null,
    };

    public static SpkLayout ForEvent(uint type) => Events.FirstOrDefault(l => l.Kind == type) ?? OtherEvent;

    public static SpkLayout ForResource(uint kind) => Resources.FirstOrDefault(l => l.Kind == kind) ?? OtherResource;

    public static uint ToQ16(double value) => (uint)(int)Math.Round(value * One);

    public static double FromQ16(uint value) => (int)value / (double)One;
}
