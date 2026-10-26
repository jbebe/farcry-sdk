using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;
using JackAll.Tools.Xbm;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Xbm;

/// <summary>The shader schema the game ships.</summary>
internal static class GameMaterials
{
    /// <summary>The vanilla schema, read without building the merged index.</summary>
    public static ShaderSchema Schema(GameCommandSettings settings)
    {
        using GameVfs vfs = ModPipeline.OpenOriginals(
            settings.OpenInstall(), BundledAssets.LoadNames(), new SyncProgress(JsonOutput.Report));
        return Parse(vfs.ReadOriginal(NameHash.Compute(ShaderSchema.GamePath)));
    }

    /// <summary>The schema the merged files hold, a mod's override included.</summary>
    public static ShaderSchema Schema(GameVfs vfs) => Parse(vfs.ReadByPath(ShaderSchema.GamePath));

    private static ShaderSchema Parse(byte[]? descriptors)
        => ShaderSchema.Parse(descriptors ?? throw new InvalidOperationException($"The install has no {ShaderSchema.GamePath}."));
}
/// <summary>Prints a material's shader and every property, as text <c>xbm set</c> takes back.</summary>
public sealed class XbmShowCommand : CliCommand<XbmShowCommand.Settings>
{
    public sealed class Settings : CommandSettings, IJsonOutputSettings
    {
        [CommandArgument(0, "<file.xbm>")]
        public string File { get; init; } = string.Empty;

        [CommandOption("--json")]
        [Description("Emit one JSON object on stdout instead of human-readable output.")]
        public bool Json { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        XbmFile material = XbmFile.Parse(CliIO.ReadInput(settings.File));
        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                name = material.Name,
                shader = material.Shader,
                properties = material.Entries.Select(e => new { key = e.Key, section = e.Section.ToString(), value = e.Text }),
            });
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"[bold]{material.Name}[/]  {material.Shader}");
        var table = new Table().AddColumns("key", "value");
        foreach (XbmEntry entry in material.Entries)
        {
            table.AddRow(Markup.Escape(entry.Key), Markup.Escape(entry.Text));
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>Writes a new material: a copy of another under a new name, or an empty one of a shader.</summary>
public sealed class XbmNewCommand : CliCommand<XbmNewCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandArgument(0, "<out.xbm>")]
        [Description("Where to write it, e.g. inside a mod layer at the game path it will have.")]
        public string Out { get; init; } = string.Empty;

        [CommandOption("--name <NAME>")]
        [Description("The material's own name, upper case by the shipped convention.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--from <file.xbm>")]
        [Description("A material to copy every property from.")]
        public string? From { get; init; }

        [CommandOption("--shader <name>")]
        [Description("Start empty on this shader instead; checked against the install's shaders.")]
        public string? Shader { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return ValidationResult.Error("--name is required.");
            }
            if ((From is null) == (Shader is null))
            {
                return ValidationResult.Error("Give exactly one of --from and --shader.");
            }
            return From is not null ? ValidationResult.Success() : base.Validate();
        }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        XbmFile material;
        if (settings.From is { } from)
        {
            material = XbmFile.Parse(CliIO.ReadInput(from));
            material.Name = settings.Name;
        }
        else
        {
            ShaderSchema schema = GameMaterials.Schema(settings);
            if (schema.Find(settings.Shader!) is null)
            {
                throw new InvalidOperationException(
                    $"No shader is named '{settings.Shader}'. The game has {string.Join(", ", schema.Names)}.");
            }
            material = XbmFile.Create(settings.Name, settings.Shader!);
        }

        CliIO.WriteOutput(settings.Out, material.Write());
        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, output = settings.Out });
            return 0;
        }
        CliIO.ReportWrote(settings.Out);
        return 0;
    }
}

/// <summary>Sets and removes a material's properties, checked against its shader.</summary>
public sealed class XbmSetCommand : CliCommand<XbmSetCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandArgument(0, "<file.xbm>")]
        public string File { get; init; } = string.Empty;

        [CommandArgument(1, "[Key=value]")]
        [Description("A property to set: a texture path, a whole number, or comma-separated floats, where "
                   + "one float fills every component. Repeatable.")]
        public string[] Assignments { get; init; } = [];

        [CommandOption("--remove <Key>")]
        [Description("A property to drop, so the shader's own default applies. Repeatable.")]
        public string[] Remove { get; init; } = [];

        [CommandOption("--no-link")]
        [Description("Set a weapon or vehicle colour alone, without its Clean twin - which the game's "
                   + "condition overwrites it from.")]
        public bool NoLink { get; init; }

        [CommandOption("-o|--out <file.xbm>")]
        [Description("Write here instead of over the input.")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        XbmFile material = XbmFile.Parse(CliIO.ReadInput(settings.File));
        ShaderDescriptor shader = GameMaterials.Schema(settings).Find(material.Shader)
            ?? throw new InvalidOperationException($"No shader is named '{material.Shader}'.");

        List<string> written = [];
        foreach (string assignment in settings.Assignments)
        {
            int at = assignment.IndexOf('=');
            if (at <= 0)
            {
                throw new FormatException($"'{assignment}' is not Key=value.");
            }
            written.AddRange(MaterialEdit.Set(material, shader, assignment[..at].Trim(), assignment[(at + 1)..], !settings.NoLink));
        }
        foreach (string key in settings.Remove)
        {
            if (!material.Remove(key))
            {
                throw new KeyNotFoundException($"'{material.Name}' has no '{key}' to remove.");
            }
        }

        string output = settings.Out ?? settings.File;
        CliIO.WriteOutput(output, material.Write());
        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, output, written, removed = settings.Remove });
            return 0;
        }
        foreach (string key in written)
        {
            AnsiConsole.MarkupLineInterpolated($"set {key} = {material.Find(key)!.Text}");
        }
        CliIO.ReportWrote(output);
        return 0;
    }
}

/// <summary>Reports what a material asks for that the game will not do.</summary>
public sealed class XbmLintCommand : CliCommand<XbmLintCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandArgument(0, "<file.xbm>")]
        [Description("The materials to check. Repeatable.")]
        public string[] Files { get; init; } = [];

        [CommandOption("-l|--layer <dir>")]
        [Description("A mod layer whose textures count as present, lowest priority first. Repeatable.")]
        public string[] Layers { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        using GameVfs vfs = settings.OpenVfs([.. settings.Layers.Select(ModPipeline.OpenLayer)]);
        ShaderSchema schema = GameMaterials.Schema(vfs);
        var findings = settings.Files.Select(file => (
            File: file,
            Problems: MaterialLint.Check(
                XbmFile.Parse(CliIO.ReadInput(file)), schema,
                path => vfs.Files.ContainsKey(NameHash.Compute(path))))).ToList();
        int count = findings.Sum(f => f.Problems.Count);

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = count == 0,
                files = findings.Select(f => new
                {
                    file = f.File,
                    problems = f.Problems.Select(p => new { key = p.Key, message = p.Message }),
                }),
            });
            return count == 0 ? 0 : 1;
        }

        foreach ((string file, List<MaterialProblem> problems) in findings)
        {
            string status = problems.Count == 0 ? "[green]ok[/]" : $"[yellow]{problems.Count} problem(s)[/]";
            AnsiConsole.MarkupLine($"{status}  {file.EscapeMarkup()}");
            foreach (MaterialProblem problem in problems)
            {
                AnsiConsole.MarkupLineInterpolated($"  {problem.Message}");
            }
        }
        return count == 0 ? 0 : 1;
    }
}
