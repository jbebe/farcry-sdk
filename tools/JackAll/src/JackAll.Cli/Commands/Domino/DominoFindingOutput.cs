using JackAll.Tools.Domino;
using JackAll.Tools.World;
using Spectre.Console;

namespace JackAll.Cli.Commands.Domino;

/// <summary>How <c>domino check</c> and <c>mod lint</c> show a graph's findings.</summary>
internal static class DominoFindingOutput
{
    public static void Print(string header, IEnumerable<DominoFinding> findings)
    {
        AnsiConsole.MarkupLine($"[blue]{header.EscapeMarkup()}[/]");
        foreach (DominoFinding f in findings)
        {
            string colour = f.Severity == LintSeverity.Error ? "red" : "yellow";
            string where = f.Function is null ? "" : $"{f.Function}: ";
            AnsiConsole.MarkupLine($"  [{colour}]{f.Rule}[/] {where.EscapeMarkup()}{f.Message.EscapeMarkup()}");
        }
    }

    public static object Json(DominoFinding f) => new { severity = f.Severity.ToString(), f.Rule, f.Message, f.Function, f.Position };
}
