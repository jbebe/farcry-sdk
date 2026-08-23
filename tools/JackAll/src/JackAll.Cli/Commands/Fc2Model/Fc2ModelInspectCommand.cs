using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Fc2Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fc2Model;

/// <summary>Lists what a pack holds, and which of it an editor has changed.</summary>
public sealed class Fc2ModelInspectCommand : CliCommand<Fc2ModelInspectCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandArgument(0, "<file.fc2model>")]
        [Description("The pack to inspect.")]
        public string Pack { get; init; } = string.Empty;

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout instead of human-readable output.")]
        public bool Json { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        Fc2ModelBundle bundle = Fc2ModelBundle.Load(settings.Pack);

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                model = bundle.Manifest.Model,
                entries = bundle.Manifest.Entries.Select(entry => new
                {
                    entry.Path, entry.Kind, entry.Role, entry.Usage, modified = entry.Modified,
                }),
            });
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"[bold]{bundle.Manifest.Model}[/]");
        var table = new Table().AddColumns("kind", "role", "changed", "path");
        foreach (Fc2ModelEntry entry in bundle.Manifest.Entries)
        {
            table.AddRow(
                entry.Kind,
                entry.Role,
                entry.Modified ? "yes" : "",
                Markup.Escape(entry.Path));
        }
        AnsiConsole.Write(table);
        return 0;
    }
}
