using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;

namespace JackAll.Cli.Commands.Hkx;

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
