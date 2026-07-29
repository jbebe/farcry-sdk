using System.ComponentModel;
using Domino.Core;
using Domino.Core.Graphs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Domino.Cli.Commands;

/// <summary>
/// Batch-classifies every `user\*.lua` mission graph's statements with <see cref="UserGraphParser"/> and
/// reports how many statements fell through to <see cref="OtherStmt"/> — the signal that a real idiom in
/// the corpus isn't covered by the six recognized shapes yet.
/// </summary>
public sealed class ClassifyGraphsCommand : Command<ClassifyGraphsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("Directory to recursively scan for .lua files (e.g. the extracted domino\\user corpus).")]
        public string Dir { get; init; } = null!;

        [CommandOption("--show-other")]
        [Description("Print each OtherStmt encountered, not just the summary counts.")]
        public bool ShowOther { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(settings.Dir, "*.lua", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int filesOk = 0;
        long totalStmts = 0, otherStmts = 0;
        var otherByType = new Dictionary<string, int>();
        var failures = new List<(string File, string Message)>();

        foreach (var file in files)
        {
            string source = File.ReadAllText(file);
            try
            {
                var root = DominoLuaSource.Parse(source);
                var graph = UserGraphParser.Parse(root);
                filesOk++;

                foreach (var fn in graph.Functions)
                {
                    foreach (var s in fn.Body)
                    {
                        totalStmts++;
                        if (s is OtherStmt other)
                        {
                            otherStmts++;
                            string key = other.Statement.GetType().Name;
                            otherByType[key] = otherByType.GetValueOrDefault(key) + 1;
                            if (settings.ShowOther)
                            {
                                Console.WriteLine($"{file} {fn.Name}: {other.Statement}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add((file, ex.Message));
            }
        }

        AnsiConsole.MarkupLine($"Classified [green]{filesOk}[/]/{files.Count} files.");
        AnsiConsole.MarkupLine($"{totalStmts} function-body statements, [yellow]{otherStmts}[/] unclassified ({(totalStmts == 0 ? 0 : 100.0 * otherStmts / totalStmts):F2}%).");
        foreach (var (type, count) in otherByType.OrderByDescending(kv => kv.Value))
        {
            AnsiConsole.MarkupLine($"  {count,6}  {type}");
        }
        foreach (var (file, message) in failures)
        {
            AnsiConsole.MarkupLine($"[red]FAIL[/] {file.EscapeMarkup()}: {message.EscapeMarkup()}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
