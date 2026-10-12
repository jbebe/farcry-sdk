using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Prints an .hkx's bodies, shapes and constraints as a tree, each by the number the edit commands take.</summary>
public sealed class HkxTreeCommand : CliCommand<HkxTreeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The Havok file.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        HkxFile file = HkxFile.Parse(CliIO.ReadInput(settings.Input));
        Dictionary<HkxObject, int> numbers = HkxDump.Numbers(file);
        foreach (HkxNode root in HkxShapes.Read(file))
        {
            Print(root, 0, numbers);
        }
        return 0;
    }

    private static void Print(HkxNode node, int depth, IReadOnlyDictionary<HkxObject, int> numbers)
    {
        string number = node.Object is { } o ? $"#{numbers[o]} " : "";
        string at = node.Primitive is { } p && p.Kind != HkxShapeKind.Mesh ? $" at {Format(p.ToModel.Translation)}" : "";
        Console.WriteLine($"{new string(' ', depth * 2)}{number}{node.Label}{at}");
        foreach (HkxNode child in node.Children)
        {
            Print(child, depth + 1, numbers);
        }
    }

    private static string Format(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})");
}
