using System.ComponentModel;
using Domino.Core.Lua;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Domino.Cli.Commands;

/// <summary>
/// Batch-parses every <c>.lua</c> file under a directory (the corpus extracted from <c>common.fat</c>'s
/// <c>domino\</c> tree) with <see cref="LuaParser"/> and reports failures. This is Phase 1's go/no-go
/// checkpoint: the parser only earns trust once it clears every real file the game ships, not a
/// hand-picked sample.
/// </summary>
public sealed class ParseAllCommand : Command<ParseAllCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("Directory to recursively scan for .lua files (e.g. the extracted domino\\ corpus).")]
        public string Dir { get; init; } = null!;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(settings.Dir, "*.lua", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int ok = 0;
        var failures = new List<(string File, string Message)>();

        foreach (var file in files)
        {
            string source = File.ReadAllText(file);
            try
            {
                LuaParser.Parse(source);
                ok++;
            }
            catch (Exception ex)
            {
                failures.Add((file, ex.Message));
            }
        }

        AnsiConsole.MarkupLine($"Parsed [green]{ok}[/]/{files.Count} files.");
        foreach (var (file, message) in failures)
        {
            AnsiConsole.MarkupLine($"[red]FAIL[/] {file.EscapeMarkup()}: {message.EscapeMarkup()}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
