using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Bark;

/// <summary>One spoken line: who says it, how, and the sound events in the bank's
/// <see cref="BarkBank.SoundsPath"/> that voice it.</summary>
public sealed record BarkLine(string Speaker, string Emotion, string Gesture, IReadOnlyList<uint> SoundIds);

/// <summary>One bark of a bank. <see cref="Block"/> is the tag PlayBark asks for (`GREET`),
/// <see cref="Mission"/> the speaker's `MIS` state it answers to, and <see cref="Lines"/> every
/// variation's lines in order.</summary>
public sealed record BarkEntry(string Block, string Source, string Target, string? Mission, IReadOnlyList<BarkLine> Lines);

/// <summary>
/// A <c>scripts\game\barkdata\&lt;N&gt;.bank</c>: FCB <c>Bark</c> objects whose lines name sound events
/// in <c>loc\&lt;N&gt;.spk</c> beside it. <see cref="ListPath"/> names every single-player bank.
/// </summary>
public static class BarkBank
{
    public const string ListPath = @"scripts\game\barkdata\spbarkdata.banklist";

    public static string BankPath(uint bank) => $@"scripts\game\barkdata\{bank}.bank";

    public static string SoundsPath(uint bank) => $@"scripts\game\barkdata\loc\{bank}.spk";

    private static readonly uint BarkType = FcbClassDefinitions.Crc32Ascii("Bark");
    private static readonly uint StateType = FcbClassDefinitions.Crc32Ascii("State");
    private static readonly uint SourceStatesType = FcbClassDefinitions.Crc32Ascii("SourceActorStates");
    private static readonly uint LineType = FcbClassDefinitions.Crc32Ascii("Line");
    private static readonly uint BankField = FcbClassDefinitions.Crc32Ascii("Bank");
    private static readonly uint SoundIdField = FcbClassDefinitions.Crc32Ascii("SoundID");

    // Text fields whose names are unknown; each sits beside a named hash of itself (BarkEventTag…).
    private const uint BlockText = 0x5809CB0D;
    private const uint SourceText = 0x8E1C6995;
    private const uint TargetText = 0x0CAB6896;
    private const uint StateText = 0x1769E6D6;
    private const uint StateValueText = 0xA0ABA078;
    private const uint SpeakerText = 0xEBAE50B1;
    private const uint EmotionText = 0xA7521067;
    private const uint GestureText = 0x489CAC93;

    /// <summary>The bank numbers a bank list names.</summary>
    public static IReadOnlyList<uint> ReadList(byte[] banklist) =>
        [.. Barks(banklist)
            .Select(c => FcbEntityFields.ReadU32(c, BankField))
            .OfType<uint>()];

    /// <summary>A bank's barks; empty when it doesn't parse.</summary>
    public static IReadOnlyList<BarkEntry> Read(byte[] bank) => [.. Barks(bank).Select(ReadBark)];

    /// <summary>What a PlayBark of <paramref name="block"/> can say, across the matching barks.</summary>
    public static IReadOnlyList<BarkLine> LinesOf(IEnumerable<BarkEntry> barks, string? block) =>
        [.. barks.Where(b => b.Block == block).SelectMany(b => b.Lines)];

    private static IEnumerable<FcbObject> Barks(byte[] fcb) =>
        FcbDocument.TryDeserialize(fcb)?.Children.Where(c => c.TypeHash == BarkType) ?? [];

    private static BarkEntry ReadBark(FcbObject bark)
    {
        string? mission = bark.Children
            .Where(c => c.TypeHash == SourceStatesType)
            .SelectMany(c => c.Children)
            .Where(s => s.TypeHash == StateType && FcbEntityFields.ReadString(s, StateText) == "MIS")
            .Select(s => FcbEntityFields.ReadString(s, StateValueText))
            .FirstOrDefault();

        return new BarkEntry(
            FcbEntityFields.ReadString(bark, BlockText),
            FcbEntityFields.ReadString(bark, SourceText),
            FcbEntityFields.ReadString(bark, TargetText),
            mission,
            [.. Descendants(bark).Where(o => o.TypeHash == LineType).Select(ReadLine)]);
    }

    private static BarkLine ReadLine(FcbObject line) => new(
        FcbEntityFields.ReadString(line, SpeakerText),
        FcbEntityFields.ReadString(line, EmotionText),
        FcbEntityFields.ReadString(line, GestureText),
        [.. Descendants(line)
            .Select(o => FcbEntityFields.ReadU32(o, SoundIdField))
            .OfType<uint>()]);

    private static IEnumerable<FcbObject> Descendants(FcbObject node) =>
        node.Children.SelectMany(c => Descendants(c).Prepend(c));
}

/// <summary>Which bank holds each mission tag's barks, read once from every bank the list names.</summary>
public sealed class BarkBankIndex
{
    private readonly Dictionary<string, (uint Bank, IReadOnlyList<BarkEntry> Barks)> _byMission;

    private BarkBankIndex(Dictionary<string, (uint, IReadOnlyList<BarkEntry>)> byMission) => _byMission = byMission;

    /// <summary>A missing list or bank is skipped.</summary>
    public static BarkBankIndex Load(Func<string, byte[]?> read)
    {
        var byMission = new Dictionary<string, (uint, IReadOnlyList<BarkEntry>)>(StringComparer.OrdinalIgnoreCase);
        if (read(BarkBank.ListPath) is not { } list)
        {
            return new BarkBankIndex(byMission);
        }

        foreach (uint bank in BarkBank.ReadList(list))
        {
            IReadOnlyList<BarkEntry> barks = read(BarkBank.BankPath(bank)) is { } bytes ? BarkBank.Read(bytes) : [];

            foreach (string mission in barks.Select(b => b.Mission).OfType<string>().Distinct())
            {
                byMission.TryAdd(mission, (bank, barks));
            }
        }
        return new BarkBankIndex(byMission);
    }

    public (uint Bank, IReadOnlyList<BarkEntry> Barks)? ForMission(string mission) =>
        _byMission.TryGetValue(mission, out var found) ? found : null;
}
