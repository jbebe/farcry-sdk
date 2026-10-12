using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
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
    [Description("The shape, as #n from hkx tree: a box, hull, sphere or capsule, or the translate or transform shape around it.")]
    public string Shape { get; init; } = null!;

    [CommandOption("-o|--out <file.hkx>")]
    [Description("Output path (default: the input, edited in place).")]
    public string? Out { get; init; }
}

/// <summary>What the hkx commands share: naming an object, and refreshing, checking and writing an edited file.</summary>
internal static class HkxCli
{
    /// <summary>The object <paramref name="spec"/> names: <c>#n</c> as <c>hkx dump</c> and <c>hkx tree</c> number it, or a body's name.</summary>
    public static HkxObject Find(HkxFile file, string spec)
    {
        List<HkxObject> objects = [.. file.Objects()];
        if (spec.StartsWith('#'))
        {
            return int.TryParse(spec[1..], out int n) && n >= 0 && n < objects.Count
                ? objects[n]
                : throw new ArgumentException($"There is no object {spec}; the file has #0 to #{objects.Count - 1}.");
        }
        List<HkxObject> bodies = [.. objects.Where(o => o.Class.Name == "hkpRigidBody" && o.GetString("name") == spec)];
        return bodies.Count switch
        {
            1 => bodies[0],
            0 => throw new ArgumentException($"No body is named '{spec}'; name an object by its number, as #n."),
            _ => throw new ArgumentException($"{bodies.Count} bodies are named '{spec}'; name the one meant by its number, as #n."),
        };
    }

    /// <summary>The convex shape <paramref name="spec"/> names, by its own number or its translate or transform shape's, and where it lands.</summary>
    public static (HkxLink Link, HkxPrimitive Primitive) Shape(HkxFile file, string spec)
    {
        HkxObject o = Find(file, spec);
        return HkxShapes.Read(file).SelectMany(n => n.Descendants())
            .FirstOrDefault(n => n is { Link: { } link, Primitive: not null } && (n.Object == o || link.Target == o)) is { } node
                ? (node.Link!.Value, node.Primitive!)
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
    /// Brings the caches of <paramref name="file"/> in line with how its shapes changed since <paramref name="before"/>,
    /// checks it, and writes it unless the check found an error. 0 when written.
    /// </summary>
    public static int Save(HkxFile file, HkxFile before, string outPath)
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
                HkxProblemSeverity.Error => "[red]error[/]",
                HkxProblemSeverity.Warning => "[yellow]warning[/]",
                _ => "[grey]note[/]",
            };
            AnsiConsole.MarkupLine($"  {label} {problem.Message.EscapeMarkup()}");
        }
        return problems.All(p => p.Severity != HkxProblemSeverity.Error);
    }
}
