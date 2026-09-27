using JackAll.Cli.Infrastructure;
using JackAll.Tools.Ai;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Xml.Linq;

namespace JackAll.Cli.Commands.Ai;

/// <summary>Lists the parameters a brain sets that their task class never reads.</summary>
public sealed class AiLintCommand : CliCommand<AiLintCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.ai.rml>")]
        [Description("The brain workspace to check.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        AiBrainGraph graph = new(AiWorkspaceFile.Read(CliIO.ReadInput(settings.Input)).Source);
        AiTaskSchema schema = AiTaskSchema.Bundled;

        var unread = graph.Nodes
            .Where(n => schema.Knows(n.Class))
            .SelectMany(n => n.Element.Elements("Parameter")
                .Select(p => (string)p.Attribute("Name")!)
                .Where(p => schema.Find(n.Class, p) is null)
                .Select(p => (n.Class, Parameter: p)))
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ToList();

        Console.WriteLine($"{graph.Count:N0} nodes; {unread.Sum(g => g.Count()):N0} parameter values the engine never reads:");
        foreach (var group in unread)
        {
            string? near = schema.ParametersOf(group.Key.Class)
                .FirstOrDefault(p => p.Name.Equals(group.Key.Parameter, StringComparison.OrdinalIgnoreCase))?.Name;
            Console.WriteLine($"  {group.Count(),6}  {group.Key.Class}.{group.Key.Parameter}{(near is null ? "" : $" (the engine reads {near})")}");
        }
        foreach (string cls in graph.Nodes.Select(n => n.Class).Distinct().Where(c => !schema.Knows(c)))
        {
            Console.WriteLine($"  unknown class {cls}");
        }
        return 0;
    }
}
