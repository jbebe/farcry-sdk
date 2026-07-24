using JackAll.Core.Format;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands;

/// <summary>
/// Regenerates <c>assets/fc2.hashlist</c> in place: every line becomes
/// <c>HHHHHHHH&lt;TAB&gt;name</c>, so <see cref="JackAll.Core.Naming.NameDatabase"/> can load the
/// ~180,000-entry file as a straight dictionary fill instead of paying for
/// <see cref="NameHash"/> on every one of them at every app launch (see perf.txt).
///
/// Rehashes every line, not just unhashed ones, so the maintenance workflow is just "append the bare
/// name on its own line, then run this command" — an already-hashed line's stored hash is discarded
/// and recomputed rather than trusted, which also makes this idempotent and self-healing if a name
/// column is ever hand-edited without updating its hash.
/// </summary>
public sealed class HashArchiveItemsCommand : Command<HashArchiveItemsCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string? path = FindHashlistPath();
        if (path is null)
        {
            AnsiConsole.MarkupLine("[red]Could not find assets\\fc2.hashlist[/] above the running executable.");
            return 1;
        }

        string[] lines = File.ReadAllLines(path);
        int rehashed = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            // Already "HASH\tname" from a previous run, or a freshly hand-added bare name -
            // either way, only the name survives; the hash is always recomputed.
            int tab = line.IndexOf('\t');
            string name = tab < 0 ? line : line[(tab + 1)..];

            uint hash = NameHash.Compute(name);
            lines[i] = $"{hash:X8}\t{NameHash.Normalize(name)}";
            rehashed++;
        }

        File.WriteAllLines(path, lines);

        AnsiConsole.MarkupLine($"Rehashed [green]{rehashed:N0}[/] entries in [green]{path.EscapeMarkup()}[/].");
        return 0;
    }

    /// <summary>Walks up from the running executable's own directory to find the repo's checked-in
    /// <c>assets/fc2.hashlist</c> — this tool edits that source file in place, not whichever build
    /// output copy happens to sit next to the exe, so a fixed relative path won't do.</summary>
    private static string? FindHashlistPath()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "assets", "fc2.hashlist");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
