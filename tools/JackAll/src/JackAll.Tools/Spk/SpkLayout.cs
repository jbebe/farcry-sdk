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

/// <summary>A sub-header word the XML names instead of writing it as <c>wN</c>.</summary>
public sealed record SpkWordField(string Name, int Index, SpkWordFormat Format);

/// <summary>
/// What each event type and resource kind means word by word: which words the XML names, which the
/// writer derives from the rest of the bank, and what an unwritten word defaults to.
/// </summary>
/// <remarks>Meanings are from docs/docs/file-formats/spk.md. A word in none of these lists is kept
/// as-is and written as <c>wN</c> when it differs from its default.</remarks>
public sealed record SpkLayout(
    string Element,
    SpkWordField[] Fields,
    int[] Derived,
    IReadOnlyDictionary<int, uint> Defaults)
{
    public const int EventWords = 17;
    public const int ResourceWords = 32;

    public const uint NoId = 0xFFFFFFFF;

    /// <summary>Resource words the sample writer fills from its audio.</summary>
    public const int SampleByteLength = 2, SampleChannels = 17, SampleRate = 19, SampleByteRate = 20,
        SampleOneShotFrames = 21, SampleOneShotBytes = 22, SampleLoopFrames = 23, SampleLoopBytes = 24,
        SampleCodec = 25;

    private static readonly Dictionary<int, uint> None = [];

    private static readonly SpkWordField Gain = new("gainDb", 5, SpkWordFormat.Q16);

    public static readonly SpkLayout Play = new("Play",
        [new("sound", 2, SpkWordFormat.Id), new("rolloff", 7, SpkWordFormat.Id)],
        [0, 1], new Dictionary<int, uint> { [4] = 0x10000, [7] = NoId });

    public static readonly SpkLayout StopNGo = new("StopNGo",
        [new("stop", 2, SpkWordFormat.Id), new("play", 3, SpkWordFormat.Id)], [0, 1], None);

    public static readonly SpkLayout SetReverb = new("SetReverb",
        [new("effect", 2, SpkWordFormat.Id)], [0, 1], None);

    public static readonly SpkLayout SwitchEvent = new("SwitchEvent",
        [new("group", 3, SpkWordFormat.Id), new("default", 4, SpkWordFormat.Id)], [0, 1, 5, 6], None);

    public static readonly SpkLayout MultiEvent = new("MultiEvent", [], [0, 1, 2, 3], None);

    public static readonly SpkLayout OtherEvent = new("Event",
        [new("target", 2, SpkWordFormat.Id)], [0, 1], None);

    public static readonly SpkLayout Sample = new("Sample",
        [Gain, new("audio", 7, SpkWordFormat.Id), new("loop", 13, SpkWordFormat.Bool)],
        [0, 1, SampleByteLength, SampleChannels, SampleRate, SampleByteRate, SampleOneShotFrames,
            SampleOneShotBytes, SampleLoopFrames, SampleLoopBytes, SampleCodec],
        new Dictionary<int, uint> { [9] = 1, [12] = 1, [28] = 7, [30] = 1, [31] = NoId });

    public static readonly SpkLayout Switch = new("Switch",
        [Gain, new("group", 8, SpkWordFormat.Id), new("default", 9, SpkWordFormat.Id)], [0, 1, 7], None);

    /// <summary>Word [8] is the weight of playing nothing: with it, the entry weights sum to 1.0.</summary>
    public static readonly SpkLayout Random = new("Random",
        [Gain, new("silence", 8, SpkWordFormat.Weight)], [0, 1, 7], None);

    public static readonly SpkLayout Multilayer = new("Multilayer", [Gain], [0, 1, 7], None);

    public static readonly SpkLayout OtherResource = new("Resource", [Gain], [0, 1], None);

    public static SpkLayout ForEvent(uint type) => type switch
    {
        (uint)SpkEventType.Leaf => Play,
        (uint)SpkEventType.StopNGo => StopNGo,
        (uint)SpkEventType.SetReverb => SetReverb,
        (uint)SpkEventType.Switch => SwitchEvent,
        (uint)SpkEventType.List => MultiEvent,
        _ => OtherEvent,
    };

    public static SpkLayout ForResource(uint kind) => kind switch
    {
        (uint)SpkResourceKind.Sample => Sample,
        (uint)SpkResourceKind.Switch => Switch,
        (uint)SpkResourceKind.Random => Random,
        (uint)SpkResourceKind.Multilayer => Multilayer,
        _ => OtherResource,
    };

    public uint Default(int index) => Defaults.GetValueOrDefault(index);
}
