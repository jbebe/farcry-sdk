namespace JackAll.Tools.Reach;

/// <summary>Which shipped game modes can reach a file, plus the report-only decoy marker.</summary>
[Flags]
public enum ReachFlags : byte
{
    None = 0,
    SP = 1,
    MP = 2,
    Editor = 4,

    /// <summary>An unused file that looks load-bearing - large, or naming many other files. Set at
    /// verdict time only, never propagated through the graph.</summary>
    Decoy = 8,
}

public static class ReachFlagsExtensions
{
    public const ReachFlags Global = ReachFlags.SP | ReachFlags.MP | ReachFlags.Editor;

    /// <summary>The mode bits alone, without <see cref="ReachFlags.Decoy"/>.</summary>
    public static ReachFlags Modes(this ReachFlags flags) => flags & Global;

    /// <summary>Renders as the report spells it: "SP|MP|EDITOR", "-" for none.</summary>
    public static string Render(this ReachFlags flags)
    {
        if (flags == ReachFlags.None)
        {
            return "-";
        }

        var parts = new List<string>(4);
        if (flags.HasFlag(ReachFlags.SP)) parts.Add("SP");
        if (flags.HasFlag(ReachFlags.MP)) parts.Add("MP");
        if (flags.HasFlag(ReachFlags.Editor)) parts.Add("EDITOR");
        if (flags.HasFlag(ReachFlags.Decoy)) parts.Add("DECOY");
        return string.Join('|', parts);
    }
}
