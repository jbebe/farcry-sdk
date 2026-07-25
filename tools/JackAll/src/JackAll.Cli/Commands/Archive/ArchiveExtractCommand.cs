using System.ComponentModel;
using System.Text;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using JackAll.Core.Naming;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Archive;

/// <summary>
/// Extracts and decompresses entries from a .fat/.dat archive to a folder — the CLI form of the App's
/// merged-filesystem browse-and-extract. With <c>--names</c>, entries with a recovered filename are
/// written to their real relative path and the rest land under <c>_unknown/&lt;hash&gt;.&lt;ext&gt;</c>
/// with an extension sniffed from their header (the same convention the App uses); without it, every
/// entry is written flat as <c>&lt;hash&gt;.&lt;ext&gt;</c>. <c>--filter</c> restricts extraction to
/// entries whose name or hash contains a substring.
/// </summary>
public sealed class ArchiveExtractCommand : CliCommand<ArchiveExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.fat>")]
        [Description("The .fat index (its sibling .dat is opened alongside it).")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out-dir <dir>")]
        [Description("Output folder (default: a folder named after the archive).")]
        public string? OutDir { get; init; }

        [CommandOption("--names")]
        [Description("Resolve hashes to real relative paths; unresolved entries go under _unknown/.")]
        public bool Names { get; init; }

        [CommandOption("--filter <substring>")]
        [Description("Only extract entries whose resolved name or hash contains this (case-insensitive).")]
        public string? Filter { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.Input))
        {
            throw new FileNotFoundException($"Input file not found: {settings.Input}");
        }

        using var archive = DuniaArchive.Open(settings.Input);
        NameDatabase? names = settings.Names ? CliAssets.LoadNames() : null;
        string outDir = settings.OutDir ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(settings.Input)) ?? ".", archive.Name);

        int written = 0, failed = 0;
        foreach (FatEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string hashHex = entry.Hash.ToString("x8");
            string? resolvedName = names is not null && names.TryResolve(entry.Hash, out string n) ? n : null;

            if (settings.Filter is { Length: > 0 } filter
                && !hashHex.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && (resolvedName is null || !resolvedName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = archive.Read(entry);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Skipped[/] {hashHex}: {ex.Message.EscapeMarkup()}");
                failed++;
                continue;
            }

            string relPath = ChooseRelativePath(resolvedName, hashHex, bytes, namesRequested: names is not null);
            CliIO.WriteOutput(Path.Combine(outDir, relPath), bytes);
            written++;
        }

        AnsiConsole.MarkupLine($"[green]Extracted[/] {written:N0} file(s) into {outDir.EscapeMarkup()}"
            + (failed > 0 ? $" ([yellow]{failed:N0} skipped[/])" : ""));
        return 0;
    }

    private static string ChooseRelativePath(string? resolvedName, string hashHex, byte[] bytes, bool namesRequested)
    {
        if (resolvedName is not null)
        {
            return SafeRelativePath(resolvedName);
        }

        string ext = FileTypeSniffer.IdentifyByContent(bytes.AsSpan(0, Math.Min(FileTypeSniffer.HeaderBytes, bytes.Length))).Extension;
        string fileName = $"{hashHex}.{ext}";
        // With --names, unnamed entries mirror the App's _unknown\ bucket; without it, everything is
        // hash-named anyway, so a flat layout is tidier than burying it all under _unknown\.
        return namesRequested ? Path.Combine("_unknown", fileName) : fileName;
    }

    /// <summary>Turns a recovered archive path (normalized, backslash-separated) into a safe relative
    /// path under the output folder — segment separators normalized, empty/'.'/'..' segments dropped so
    /// a stray name can't escape the output directory.</summary>
    private static string SafeRelativePath(string name)
    {
        string[] segments = name.Split('\\', '/');
        var kept = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                continue;
            }
            kept.Add(Sanitize(segment));
        }
        return kept.Count == 0 ? name.Replace('\\', '_').Replace('/', '_') : Path.Combine([.. kept]);
    }

    private static string Sanitize(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(segment.Length);
        foreach (char c in segment)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
