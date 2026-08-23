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

        [CommandOption("--clips")]
        [Description("List the animation banks instead of the files.")]
        public bool Clips { get; init; }

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
                clips = bundle.Manifest.Clips,
            });
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"[bold]{bundle.Manifest.Model}[/]");
        AnsiConsole.Write(settings.Clips ? ClipTable(bundle) : FileTable(bundle));
        if (!settings.Clips && bundle.Manifest.Clips.Count > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"{bundle.Manifest.Clips.Count} animation bank(s) - pass --clips to list them");
        }
        return 0;
    }

    private static Table FileTable(Fc2ModelBundle bundle)
    {
        var table = new Table().AddColumns("kind", "role", "changed", "path");
        foreach (Fc2ModelEntry entry in bundle.Manifest.Entries)
        {
            table.AddRow(
                entry.Kind,
                entry.Role,
                entry.Modified ? "yes" : "",
                Markup.Escape(entry.Path));
        }
        return table;
    }

    /// <summary>
    /// The banks, with the bone each hangs the model from - the fact that says where geometry
    /// belongs, and the reason a bank is worth carrying at all.
    /// </summary>
    private static Table ClipTable(Fc2ModelBundle bundle)
    {
        var table = new Table().AddColumns("frames", "rate", "bone", "clip");
        foreach (Fc2ModelClip clip in bundle.Manifest.Clips)
        {
            table.AddRow(
                clip.Frames.ToString(),
                clip.Rate.ToString(),
                Markup.Escape(clip.Bone ?? ""),
                Markup.Escape(clip.Label));
        }
        return table;
    }
}
