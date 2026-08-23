using JackAll.Tools.Mab;

namespace JackAll.Tools.Fc2Model;

/// <summary>
/// What a bank attaches besides its own skeleton, and where.
/// </summary>
/// <remarks>
/// Derived on the way in and ignored on the way out - the tag block those bytes came from travels
/// whole, because 140 of the 172 bytes in a record are still undecoded and only the block itself can
/// rebuild the file. This is here so a reader never has to open that block: without it the one thing
/// a modeler needs from a bank - which bone the gun hangs from - is unreadable outside JackAll.
/// </remarks>
public sealed class BankParticipant
{
    /// <summary>The model's file stem, which is how a bank names what it moves.</summary>
    public required string Name { get; init; }

    /// <summary>The bone on the bank's own skeleton that this hangs from.</summary>
    public required string Bone { get; init; }

    public required string Reference { get; init; }

    public required int Kind { get; init; }

    /// <summary>
    /// Which clip in <see cref="BankDocument.Clips"/> moves this participant.
    /// </summary>
    /// <remarks>
    /// Record <c>k</c> names chain clip <c>k + 1</c> - the chain is one clip per skeleton taking
    /// part, in the order the records list them, which is what <see cref="MabEncoder"/> relies on
    /// when it repoints them.
    /// </remarks>
    public required int Clip { get; init; }
}

/// <summary>
/// A whole animation bank, decoded: its file header and every clip in the chain.
/// </summary>
/// <remarks>
/// A bank is not one animation - it holds one clip per skeleton taking part, so a weapon's motion
/// rides in the clip behind the character's. The chain is nested on disk; here it is a plain list,
/// and the nesting is rebuilt on the way out.
/// </remarks>
public sealed class BankDocument
{
    /// <summary>The sixteen-byte file header, which carries a version and a hash nothing derives.</summary>
    public required byte[] Header { get; init; }

    public List<ClipDocument> Clips { get; init; } = [];

    /// <summary>What the bank moves besides its own skeleton. See <see cref="BankParticipant"/>.</summary>
    public List<BankParticipant> Participants { get; init; } = [];

    public static BankDocument From(MabFile bank) => new()
    {
        Header = [.. bank.Header],
        Clips = [.. bank.Clips().Select(ClipDocument.From)],
        Participants = [.. bank.Participants().Select((record, index) => new BankParticipant
        {
            Name = record.Name,
            Bone = record.Parent,
            Reference = record.Reference,
            Kind = record.Kind,
            Clip = index + 1,
        })],
    };

    public byte[] ToMab()
        => MabEncoder.AssembleBank(Header, [.. Clips.Select(clip => clip.ToParts())]);
}
