using System.ComponentModel;
using System.Text.Json;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Xbm;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fc2Model;

public class Fc2ModelEditSettings : CommandSettings, IJsonOutputSettings
{
    [CommandArgument(0, "<file.fc2model>")]
    public string Pack { get; init; } = string.Empty;

    [CommandOption("-o|--out <file.fc2model>")]
    [Description("Write here instead of over the input.")]
    public string? Out { get; init; }

    [CommandOption("--json")]
    [Description("Emit one JSON object on stdout instead of human-readable output.")]
    public bool Json { get; init; }

    public string Output => Out ?? Pack;
}

/// <summary>Adds a material the game does not have to a pack, for its mesh to draw with.</summary>
public sealed class Fc2ModelAddMaterialCommand : CliCommand<Fc2ModelAddMaterialCommand.Settings>
{
    public sealed class Settings : Fc2ModelEditSettings
    {
        [CommandArgument(1, "<path>")]
        [Description(@"The new material's game path, e.g. graphics\weapons\special\dart_rifle\vss_body.xbm.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--from <material>")]
        [Description("What to start from: a material the pack carries, by game path, or an .xbm file.")]
        public string From { get; init; } = string.Empty;

        [CommandOption("--name <NAME>")]
        [Description("The new material's own name (default: the source's).")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(From) ? ValidationResult.Error("--from is required.") : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        Fc2ModelBundle bundle = Fc2ModelBundle.Load(settings.Pack);
        MaterialDocument material = bundle.Entry(settings.From) is { Kind: Fc2ModelKind.Material } entry
            ? bundle.Document<MaterialDocument>(entry)
            : MaterialDocument.Parse(CliIO.ReadInput(settings.From));
        material.Name = settings.Name ?? material.Name;

        int index = bundle.AddMaterial(settings.Path, material);
        bundle.Save(settings.Output);

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, index, output = settings.Output });
            return 0;
        }
        AnsiConsole.MarkupLineInterpolated($"Added {settings.Path} as material {index}. Move clusters onto it with assign-material.");
        CliIO.ReportWrote(settings.Output);
        return 0;
    }
}

/// <summary>Moves the clusters drawing with one of the mesh's materials onto another.</summary>
public sealed class Fc2ModelAssignMaterialCommand : CliCommand<Fc2ModelAssignMaterialCommand.Settings>
{
    public sealed class Settings : Fc2ModelEditSettings
    {
        [CommandOption("--from <material>")]
        [Description("The material the clusters draw with now, by index or game path.")]
        public string From { get; init; } = string.Empty;

        [CommandOption("--to <material>")]
        [Description("The material to draw them with instead, by index or game path.")]
        public string To { get; init; } = string.Empty;

        [CommandOption("--part <NAME>")]
        [Description("Only this part, at every LOD and damage state: FRAME covers FRAME_LOD0 to FRAME_LOD4. "
                   + "Repeatable; every part when left out.")]
        public string[] Parts { get; init; } = [];

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(From) || string.IsNullOrWhiteSpace(To)
                ? ValidationResult.Error("--from and --to are both required.")
                : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        Fc2ModelBundle bundle = Fc2ModelBundle.Load(settings.Pack);
        Fc2ModelEntry entry = bundle.Mesh();
        MeshDocument mesh = bundle.Document<MeshDocument>(entry);

        int moved = mesh.Reassign(Index(mesh, settings.From), Index(mesh, settings.To), settings.Parts);
        if (moved == 0)
        {
            throw new InvalidOperationException("No cluster matched, so nothing was changed.");
        }
        bundle.Replace(entry, JsonSerializer.SerializeToUtf8Bytes(mesh, Fc2ModelJson.Compact));
        bundle.Save(settings.Output);

        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, moved, output = settings.Output });
            return 0;
        }
        AnsiConsole.MarkupLineInterpolated($"Moved {moved} cluster(s).");
        CliIO.ReportWrote(settings.Output);
        return 0;
    }

    private static int Index(MeshDocument mesh, string material)
    {
        if (int.TryParse(material, out int index))
        {
            return index;
        }
        int found = mesh.Materials.FindIndex(path => path.Equals(material, StringComparison.OrdinalIgnoreCase));
        return found >= 0 ? found : throw new KeyNotFoundException($"The mesh names no material '{material}'.");
    }
}
