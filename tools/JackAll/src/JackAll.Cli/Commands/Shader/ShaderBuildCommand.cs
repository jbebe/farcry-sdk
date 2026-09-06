using JackAll.Cli.Infrastructure;
using JackAll.Tools.Shader;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Shader;

/// <summary>
/// Reassembles a shader object from replacement bytecode and the <c>.xml</c> binding table produced
/// by <c>shader extract</c>. The table is required, not optional: the bytecode carries no CTAB, so
/// nothing in a bare <c>fxc</c> output says which register the engine feeds each parameter into.
/// </summary>
public sealed class ShaderBuildCommand : CliCommand<ShaderBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The replacement Direct3D bytecode, as fxc /Fo writes it.")]
        public string Bytecode { get; init; } = null!;

        [CommandArgument(1, "[file.xml]")]
        [Description("The binding table from `shader extract` (default: the .bin path with a .xml extension).")]
        public string? Xml { get; init; }

        [CommandOption("-o|--out <file>")]
        [Description("Output path (default: the .bin path with a .pso or .vso extension, per the profile).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string xmlPath = settings.Xml ?? Path.ChangeExtension(settings.Bytecode, ".xml");

        ShaderObject shader = ShaderObject.FromXml(
            CliIO.ReadInputText(xmlPath),
            ShaderObject.StripConstantTable(CliIO.ReadInput(settings.Bytecode)));

        string outPath = settings.Out ?? Path.ChangeExtension(
            settings.Bytecode, shader.Profile == "vs_3_0" ? ".vso" : ".pso");
        CliIO.GuardNotOverwritingInput(settings.Bytecode, outPath);

        byte[] built = shader.Build();

        // Same check a corrupt object would fail on load, run here rather than in-game.
        ShaderObject.Parse(built);

        CliIO.WriteOutput(outPath, built);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
