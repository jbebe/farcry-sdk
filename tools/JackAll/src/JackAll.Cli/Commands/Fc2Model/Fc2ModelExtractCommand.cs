using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Fc2Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fc2Model;

/// <summary>
/// Writes a pack's edits out as game files, laid out as a mod layer.
/// </summary>
/// <remarks>
/// Only what the editor changed is written; an untouched entry is left for the install's own copy,
/// which is what stops a texture being compressed again on every trip through the pack.
/// <para>
/// The result is a folder the user adds to their own mod, not a mod itself - the App applies a pack
/// directly, the CLI stops one step short of that on purpose.
/// </para>
/// </remarks>
public sealed class Fc2ModelExtractCommand : CliCommand<Fc2ModelExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandArgument(0, "<file.fc2model>")]
        [Description("The pack to write out.")]
        public string Pack { get; init; } = string.Empty;

        [CommandOption("-o|--out <dir>")]
        [Description("Where to write the layer (default: the pack's name).")]
        public string? Out { get; init; }

        [CommandOption("--all")]
        [Description("Write every entry, not only the ones an editor changed.")]
        public bool All { get; init; }

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout instead of human-readable output.")]
        public bool Json { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        Fc2ModelBundle bundle = Fc2ModelBundle.Load(settings.Pack);
        List<Fc2ModelOutput> outputs = Fc2ModelApplier.Outputs(bundle, onlyModified: !settings.All);

        string root = settings.Out ?? Path.GetFileNameWithoutExtension(settings.Pack);
        foreach (Fc2ModelOutput output in outputs)
        {
            // A layer's content lives under the reserved mods folder, and anything outside it is
            // ignored - that wrapper is the whole of the layer contract.
            string target = Path.Combine(
                root, "mods", output.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, output.Content);
        }

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, files = outputs.Count, output = root });
            return 0;
        }

        if (outputs.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Nothing to write:[/] this pack holds no edits. Pass --all to write every entry.");
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"Wrote {outputs.Count} file(s) under {root}");
        return 0;
    }
}
