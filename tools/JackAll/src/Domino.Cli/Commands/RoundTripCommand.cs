using System.ComponentModel;
using Domino.Core.Graphs;
using Domino.Core.Lua;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Domino.Cli.Commands;

/// <summary>
/// The Phase 4 checkpoint: parse -&gt; classify -&gt; write -&gt; reparse -&gt; classify -&gt; write again, and
/// check the second generated text matches the first (see <see cref="UserGraphWriter"/>'s remarks on why
/// this idempotence check, rather than a structural AST comparison, is the round-trip signal that
/// matters here).
/// </summary>
public sealed class RoundTripCommand : Command<RoundTripCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("Directory to recursively scan for .lua files (e.g. the extracted domino\\user corpus).")]
        public string Dir { get; init; } = null!;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(settings.Dir, "*.lua", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int stable = 0;
        var failures = new List<(string File, string Message)>();

        foreach (var file in files)
        {
            try
            {
                string source = File.ReadAllText(file);
                var graph1 = UserGraphParser.Parse(LuaParser.Parse(source));
                string generated1 = UserGraphWriter.Write(graph1);

                var graph2 = UserGraphParser.Parse(LuaParser.Parse(generated1));
                string generated2 = UserGraphWriter.Write(graph2);

                if (generated1 == generated2)
                {
                    stable++;
                }
                else
                {
                    failures.Add((file, "regenerated text is not stable across a second round trip"));
                }
            }
            catch (Exception ex)
            {
                failures.Add((file, ex.Message));
            }
        }

        AnsiConsole.MarkupLine($"Stable round trip: [green]{stable}[/]/{files.Count} files.");
        foreach (var (file, message) in failures)
        {
            AnsiConsole.MarkupLine($"[red]FAIL[/] {file.EscapeMarkup()}: {message.EscapeMarkup()}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
