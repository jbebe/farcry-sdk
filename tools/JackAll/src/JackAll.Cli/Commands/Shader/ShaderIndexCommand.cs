using JackAll.Cli.Infrastructure;
using JackAll.Tools.Shader;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Globalization;

namespace JackAll.Cli.Commands.Shader;

/// <summary>
/// Resolves a shader permutation to the object files it loads, through the <c>index.pso</c>,
/// <c>index.vso</c> and <c>index.rs</c> tables at the root of a <c>shadersobj</c> tree.
/// </summary>
public sealed class ShaderIndexCommand : CliCommand<ShaderIndexCommand.Settings>
{
    private static readonly string[] Kinds = ["pso", "vso", "rs"];

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<dir>")]
        [Description("A shadersobj tree's obj directory, the one holding index.pso.")]
        public string Directory { get; init; } = null!;

        [CommandOption("-s|--shader <name>")]
        [Description("Source name of a shader, e.g. celestialbody. Resolves its no-option permutation.")]
        public string? Shader { get; init; }

        [CommandOption("-k|--key <hex>")]
        [Description("A permutation key to resolve directly.")]
        public string? Key { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        var tables = Kinds.ToDictionary(
            kind => kind,
            kind => ShaderIndex.Parse(CliIO.ReadInput(Path.Combine(settings.Directory, "index." + kind))));

        if (settings.Shader is null && settings.Key is null)
        {
            foreach ((string kind, ShaderIndex table) in tables)
            {
                AnsiConsole.MarkupLine($"index.{kind}: version {table.Version}, {table.Count} permutations");
            }
            return 0;
        }

        uint key = settings.Key is { } hex
            ? uint.Parse(hex.TrimStart('0', 'x', 'X'), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ShaderIndex.KeyOf(settings.Shader!);

        AnsiConsole.MarkupLine($"key {key:x8}{(settings.Shader is null ? "" : $"  ({settings.Shader}, no options)")}");

        foreach ((string kind, ShaderIndex table) in tables)
        {
            switch (table.Lookup(key))
            {
                case null:
                    AnsiConsole.MarkupLine($"  {kind,-3}  [yellow]no such permutation[/]");
                    break;
                case 0:
                    AnsiConsole.MarkupLine($"  {kind,-3}  none");
                    break;
                case { } hash:
                    AnsiConsole.MarkupLine($"  {kind,-3}  {hash:x8}  {ShaderIndex.PathOf(hash, kind)}");
                    break;
            }
        }
        return 0;
    }
}
