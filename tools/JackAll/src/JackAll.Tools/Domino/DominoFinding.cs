using JackAll.Tools.World;

namespace JackAll.Tools.Domino;

/// <summary>One problem in a Domino user graph. <see cref="Rule"/> is a stable kebab-case name, and
/// <see cref="Position"/> the offset of the statement it is about.</summary>
public sealed record DominoFinding(LintSeverity Severity, string Rule, string Message)
{
    public string? Function { get; init; }

    public string? NodeId { get; init; }

    public int? Position { get; init; }
}
