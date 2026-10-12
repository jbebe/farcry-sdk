using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>The file, the shape and the output every shape-editing hkx command takes.</summary>
public class HkxShapeSettings : CommandSettings
{
    [CommandArgument(0, "<file.hkx>")]
    [Description("The file to edit.")]
    public string Input { get; init; } = null!;

    [CommandArgument(1, "<shape>")]
    [Description("The shape, as #n from hkx tree: a box, hull, sphere or capsule, or the translate or transform shape around one.")]
    public string Shape { get; init; } = null!;

    [CommandOption("-o|--out <file.hkx>")]
    [Description("Output path (default: the input, edited in place).")]
    public string? Out { get; init; }
}

/// <summary>What the hkx commands share: naming an object, and refreshing, checking and writing an edited file.</summary>
internal static class HkxCli
{
    /// <summary>
    /// The convex shape <paramref name="spec"/> names, as <c>#n</c> from <c>hkx tree</c>: the member placing it, where
    /// it lands in the model, and where the space that member places it in does.
    /// </summary>
    public static (HkxLink Link, Matrix4x4 ToModel, Matrix4x4 ParentToModel) Shape(HkxFile file, string spec)
    {
        List<HkxObject> objects = file.Objects();
        HkxObject o = spec.StartsWith('#') && int.TryParse(spec[1..], out int number) && number >= 0 && number < objects.Count
            ? objects[number]
            : throw new ArgumentException($"There is no object {spec}; the file has #0 to #{objects.Count - 1}.");
        return HkxShapes.Read(file).SelectMany(n => n.Descendants()).Where(n => n.Object == o).Select(HkxShapeEdit.Placed).FirstOrDefault(p => p is not null) is { } placed
            ? (placed.Link, placed.Primitive.ToModel, HkxShapeEdit.ParentToModel(placed.Link, placed.Primitive.ToModel))
            : throw new ArgumentException($"{spec} is not a drawn box, hull, sphere or capsule, or the translate or transform shape around one; hkx tree lists them.");
    }

    /// <summary>A vector typed as <c>x,y,z</c>.</summary>
    public static Vector3 Vector(string text, string option)
    {
        float[] f = [.. text.Split(',').Select(w => float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN)];
        return f.Length == 3 && f.All(float.IsFinite) ? new Vector3(f[0], f[1], f[2]) : throw new ArgumentException($"{option} takes x,y,z, not '{text}'.");
    }

    /// <summary>Reads a file, changes it, and saves it as <see cref="Save"/> does: over the input unless <paramref name="output"/> names another path.</summary>
    public static int Edit(string input, string? output, Action<HkxFile> change)
    {
        byte[] bytes = CliIO.ReadInput(input);
        HkxFile file = HkxFile.Parse(bytes);
        change(file);
        return Save(file, HkxFile.Parse(bytes), output ?? input);
    }

    /// <summary>
    /// Brings the caches of <paramref name="file"/> in line with how its shapes changed since <paramref name="before"/>
    /// (only its copies, without one), checks it, and writes it unless the check found an error. 0 when written.
    /// </summary>
    public static int Save(HkxFile file, HkxFile? before, string outPath)
    {
        foreach (string written in HkxCaches.Refresh(file, before))
        {
            AnsiConsole.MarkupLine($"  [grey]refreshed[/] {written.EscapeMarkup()}");
        }
        if (!Report(HkxLint.Check(file)))
        {
            return 1;
        }
        byte[] bytes = file.Write();
        // A read-back check: a file that fails to parse is never written.
        HkxFile.Parse(bytes);
        CliIO.WriteOutput(outPath, bytes);
        CliIO.ReportWrote(outPath);
        return 0;
    }

    /// <summary>Prints what <see cref="HkxLint"/> found; false when any of it is an error.</summary>
    public static bool Report(IReadOnlyList<HkxProblem> problems)
    {
        foreach (HkxProblem problem in problems)
        {
            string label = problem.Severity switch
            {
                LintSeverity.Error => "[red]error[/]",
                LintSeverity.Warning => "[yellow]warning[/]",
                _ => "[grey]note[/]",
            };
            AnsiConsole.MarkupLine($"  {label} {problem.Message.EscapeMarkup()}");
        }
        return problems.All(p => p.Severity != LintSeverity.Error);
    }
}
