using System.ComponentModel;
using Domino.Core.Lua;
using Domino.Core.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Domino.Cli.Commands;

/// <summary>
/// Batch-parses every `system\*.lua` node's reflection-box pin metadata and reports failures/misses —
/// the checkpoint for <see cref="ReflectionBoxParser"/> mirroring <see cref="ParseAllCommand"/> for the
/// underlying Lua grammar.
/// </summary>
public sealed class ReflectAllCommand : Command<ReflectAllCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("Directory to recursively scan for .lua files (e.g. the extracted domino\\system corpus).")]
        public string Dir { get; init; } = null!;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(settings.Dir, "*.lua", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int ok = 0, missing = 0;
        var failures = new List<(string File, string Message)>();

        foreach (var file in files)
        {
            string source = File.ReadAllText(file);
            try
            {
                var chunk = LuaParser.Parse(source);
                var reflection = ReflectionBoxParser.Parse(chunk);
                if (reflection is null)
                {
                    missing++;
                    failures.Add((file, "no reflection box found"));
                }
                else
                {
                    ok++;
                }
            }
            catch (Exception ex)
            {
                failures.Add((file, ex.Message));
            }
        }

        AnsiConsole.MarkupLine($"Reflected [green]{ok}[/]/{files.Count} files ([yellow]{missing}[/] missing a box).");
        foreach (var (file, message) in failures)
        {
            AnsiConsole.MarkupLine($"[red]FAIL[/] {file.EscapeMarkup()}: {message.EscapeMarkup()}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
