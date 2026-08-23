using JackAll.Tools.Mab;

namespace JackAll.Tools.Fc2Model;

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

    public static BankDocument From(MabFile bank) => new()
    {
        Header = [.. bank.Header],
        Clips = [.. bank.Clips().Select(ClipDocument.From)],
    };

    public byte[] ToMab()
        => MabEncoder.AssembleBank(Header, [.. Clips.Select(clip => clip.ToParts())]);
}
