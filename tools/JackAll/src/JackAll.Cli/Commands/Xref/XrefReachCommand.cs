using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Reach;
using JackAll.Tools.Xrefs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Xref;

/// <summary>
/// Classifies every corpus file by whether the engine could ever read it: seeds the roots Dunia.dll
/// itself names (assets/engine-roots.tsv), walks the reference graph to a fixpoint, and writes a
/// verdict list whose headline is the decoy table - unused files shaped like something the game
/// depends on.
/// </summary>
public sealed class XrefReachCommand : CliCommand<XrefReachCommand.Settings>
{
    public sealed class Settings : XrefFileSettings
    {
        [CommandOption("--roots <file>")]
        [Description("The engine-roots asset (default: the bundled assets/engine-roots.tsv).")]
        public string? RootsPath { get; init; }

        [CommandOption("--out <file>")]
        [Description("Where to write the verdict TSV (default: fc2.reach.tsv in the current directory).")]
        public string? OutPath { get; init; }

        [CommandOption("--only <verdicts>")]
        [Description("Write only these comma-separated verdicts (used, used-sp-only, used-mp-only, unused, unknown). "
                   + "The whole corpus is 22 MB of TSV; the checked-in asset is the 'unused,unknown' slice.")]
        public string? Only { get; init; }

        [CommandOption("--build")]
        [Description("Build (or refresh) the reference index first instead of requiring an existing one.")]
        public bool Build { get; init; }

        [CommandOption("--audit <count>")]
        [Description("Print this many seeded-random 'unused' rows for manual tracing.")]
        public int Audit { get; init; }

        [CommandOption("--explain <path>")]
        [Description("Print how this game-relative path was (or wasn't) reached, then exit.")]
        public string? Explain { get; init; }

        [CommandOption("--seed <path>")]
        [Description("Extra path to seed as a GLOBAL root, for experiments. Repeatable.")]
        public string[]? Seeds { get; init; }

        [CommandOption("--allow-modified")]
        [Description("Analyse even though JackAll has deployed mods into this install's patch archive.")]
        public bool AllowModified { get; init; }
    }

    // Mirrors TextReferenceExtractor.MaxTextBytes: text files past it contribute no edges, so any
    // reachable one that big is a potential source of false `unused` verdicts downstream.
    private const int TextCapBytes = 4 * 1024 * 1024;

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        if (install.HasVanillaBackup && !settings.AllowModified)
        {
            throw new InvalidOperationException(
                "This install's patch archive is JackAll build output, so verdicts would describe the modded "
                + "install, not the game. Restore vanilla first, or pass --allow-modified.");
        }

        var progress = new SyncProgress(JsonOutput.Report);
        using GameVfs vfs = GameVfs.Load(
            install, BundledAssets.LoadNames(), GameCache.Load(install.CacheFile),
            BundledAssets.LoadFcbClasses(), progress, includeFragments: false);

        string indexPath = settings.ResolveIndexPath();
        ReferenceIndex index;
        if (settings.Build)
        {
            index = ReferenceIndexer.BuildBaseIndex(
                vfs, ReferenceExtractors.All, ReferenceIndex.Load(indexPath), progress, cancellationToken).Index;
            index.Save(indexPath);
        }
        else
        {
            index = ReferenceIndex.Load(indexPath);
            if (index.EdgeCount == 0)
            {
                throw new InvalidOperationException(
                    $"No usable reference index at {indexPath} - an empty index would mark every file unused. "
                    + "Run 'xref build' first, or pass --build.");
            }
        }

        // patch.dat entries are session-indexed by design (the archive is JackAll's deploy target),
        // and EntityLibraryPatchOverride.fcb lives there - the overlay is not optional here.
        ReferenceHarvest overlay = ReferenceIndexer.HarvestOverlay(vfs, ReferenceExtractors.All, cancellationToken);
        var graph = new ReferenceGraph(index, overlay);

        string rootsPath = settings.RootsPath
            ?? BundledAssets.FindAsset(".engineroots", Path.Combine("assets", "engine-roots.tsv"))
            ?? throw new InvalidOperationException("engine-roots.tsv not found beside the executable or in assets\\.");
        IEnumerable<string> rootLines = File.ReadLines(rootsPath);
        if (settings.Seeds is { Length: > 0 } seeds)
        {
            rootLines = rootLines.Concat(seeds.Select(s => $"literal\tGLOBAL\t{s}"));
        }
        EngineRoots roots = EngineRoots.Parse(rootLines);

        List<ReachFile> corpus = [.. vfs.Files.Values
            .Where(f => !f.IsSynthetic)
            .Select(f => new ReachFile(f.EngineHash, f.Path, f.Type.Extension, f.Size, f.SourceName, f.NameIsKnown))];

        ReachResult result = ReachAnalysis.Run(corpus, graph, roots);

        if (settings.Explain is { Length: > 0 } explain)
        {
            return ExplainChain(result, explain);
        }

        List<GroundTruth> truths = ReachReport.CheckGroundTruths(result.Rows);
        truths.Add(CheckMoveClips(result, graph));

        string outPath = Path.GetFullPath(settings.OutPath ?? "fc2.reach.tsv");
        IReadOnlyList<ReachRow> written = result.Rows;
        if (settings.Only is { Length: > 0 } only)
        {
            var keep = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            written = [.. result.Rows.Where(r => keep.Contains(ReachReport.VerdictText(r.Verdict)))];
        }
        ReachReport.WriteTsv(outPath, written);

        IReadOnlyList<ReachRow> decoys = ReachReport.Decoys(result.Rows);
        var verdictCounts = result.Rows.CountBy(r => r.Verdict).ToDictionary();
        var bytesByVerdict = result.Rows
            .GroupBy(r => r.Verdict)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.File.Size));
        List<string> textOverCap = [.. result.Rows
            .Where(r => r.File.Extension is "xml" or "lua" or "desc" or "txt" or "ini"
                        && r.File.Size > TextCapBytes)
            .Select(r => r.File.Path)];

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                @out = outPath,
                corpusFiles = result.Rows.Count,
                named = result.Rows.Count(r => r.File.NameIsKnown),
                seededFiles = result.SeededFiles,
                verdicts = verdictCounts.ToDictionary(p => ReachReport.VerdictText(p.Key), p => p.Value),
                bytesByVerdict = bytesByVerdict.ToDictionary(p => ReachReport.VerdictText(p.Key), p => p.Value),
                decoys = decoys.Select(r => new
                {
                    path = r.File.Path,
                    bytes = r.File.Size,
                    outRefs = r.OutRefs,
                    reason = r.Reason,
                }).ToArray(),
                warnings = result.Warnings,
                guards = new
                {
                    patchIsBuildOutput = install.HasVanillaBackup,
                    nameProbeMatches = result.NameProbeMatches,
                    textFilesOverCap = textOverCap,
                },
                groundTruths = truths.Select(t => new { name = t.Name, pass = t.Pass, detail = t.Detail }).ToArray(),
            });
            return truths.All(t => t.Pass) ? 0 : 1;
        }

        // The decoy table leads: it is the answer this analysis exists to give.
        if (decoys.Count > 0)
        {
            var table = new Table().Border(TableBorder.Simple).Title("[bold]Decoys - unused but shaped like they matter[/]");
            table.AddColumn("path");
            table.AddColumn(new TableColumn("bytes").RightAligned());
            table.AddColumn(new TableColumn("outRefs").RightAligned());
            table.AddColumn("reason");
            foreach (ReachRow row in decoys.Take(40))
            {
                table.AddRow(row.File.Path.EscapeMarkup(), $"{row.File.Size:N0}", $"{row.OutRefs:N0}", row.Reason.EscapeMarkup());
            }
            AnsiConsole.Write(table);
            if (decoys.Count > 40)
            {
                AnsiConsole.MarkupLine($"[grey]... and {decoys.Count - 40:N0} more decoys in {outPath.EscapeMarkup()}[/]");
            }
        }

        AnsiConsole.MarkupLine($"[green]Classified[/] {result.Rows.Count:N0} files ({result.SeededFiles:N0} seeded from roots)");
        foreach ((ReachVerdict verdict, int count) in verdictCounts.OrderBy(p => p.Key))
        {
            AnsiConsole.MarkupLine(
                $"  {ReachReport.VerdictText(verdict),-13}: {count,7:N0}  ({bytesByVerdict[verdict] / 1024.0 / 1024.0,9:N1} MB)");
        }
        AnsiConsole.MarkupLine($"  written to   : {outPath.EscapeMarkup()}");

        foreach (string warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]warning:[/] {warning.EscapeMarkup()}");
        }
        if (textOverCap.Count > 0)
        {
            AnsiConsole.MarkupLine($"[grey]{textOverCap.Count} text file(s) exceed the extractor's 4 MB cap and contribute no edges[/]");
        }

        foreach (GroundTruth truth in truths)
        {
            AnsiConsole.MarkupLine(truth.Pass
                ? $"  [green]ok[/]   {truth.Name}: {truth.Detail.EscapeMarkup()}"
                : $"  [red]FAIL[/] {truth.Name}: {truth.Detail.EscapeMarkup()}");
        }

        if (settings.Audit > 0)
        {
            PrintAudit(result, settings.Audit);
        }

        return truths.All(t => t.Pass) ? 0 : 1;
    }

    /// <summary>The one ground truth that needs the graph: every clip movemgr.bin names must be
    /// reachable, because depload makes `.mab` loading mandatory.</summary>
    private static GroundTruth CheckMoveClips(ReachResult result, ReferenceGraph graph)
    {
        var rowByHash = result.Rows.ToDictionary(r => r.File.Hash);
        uint movemgr = NameHash.Compute(@"graphics\move\movemgr.bin");
        int total = 0, missing = 0;
        string? example = null;
        foreach (RefEdge edge in graph.ReferencesFrom(movemgr))
        {
            if (edge.Kind != RefKind.MoveClip || !rowByHash.TryGetValue(edge.Target, out ReachRow? row))
            {
                continue;
            }
            total++;
            if (row.Verdict is ReachVerdict.Unused or ReachVerdict.Unknown)
            {
                missing++;
                example ??= row.File.Path;
            }
        }
        return new GroundTruth(
            "movemgr-clips-used",
            total > 0 && missing == 0,
            total == 0 ? "no clip edges found" : missing == 0 ? $"{total} clip(s) ok" : $"{missing}/{total} not reached, e.g. {example}");
    }

    private static int ExplainChain(ReachResult result, string path)
    {
        uint hash = NameHash.Compute(path);
        ReachRow? row = result.Rows.FirstOrDefault(r => r.File.Hash == hash);
        if (row is null)
        {
            AnsiConsole.MarkupLine($"[yellow]{path.EscapeMarkup()}[/] is not in the corpus.");
            return 1;
        }

        var rowByHash = result.Rows.ToDictionary(r => r.File.Hash);
        AnsiConsole.MarkupLine(
            $"{row.File.Path.EscapeMarkup()} = [bold]{ReachReport.VerdictText(row.Verdict)}[/] ({row.Flags.Render()}) - {row.Reason.EscapeMarkup()}");
        uint at = hash;
        while (result.Predecessors.TryGetValue(at, out uint from) && from != 0 && from != at)
        {
            string reason = rowByHash.TryGetValue(from, out ReachRow? fromRow) ? fromRow.Reason : "?";
            string name = rowByHash.TryGetValue(from, out ReachRow? r) ? r.File.Path : $"#{from:X8}";
            AnsiConsole.MarkupLine($"  reached from {name.EscapeMarkup()} - {reason.EscapeMarkup()}");
            at = from;
        }
        return 0;
    }

    private static void PrintAudit(ReachResult result, int count)
    {
        // Seeded so a re-run audits the same sample - reproducibility beats variety here.
        var random = new Random(12345);
        var unused = result.Rows.Where(r => r.Verdict == ReachVerdict.Unused).ToList();
        var sample = unused.OrderBy(_ => random.Next()).Take(count).OrderBy(r => r.File.Path);
        AnsiConsole.MarkupLine($"[bold]Audit sample[/] ({Math.Min(count, unused.Count)} of {unused.Count:N0} unused):");
        foreach (ReachRow row in sample)
        {
            AnsiConsole.MarkupLine($"  {row.File.Path.EscapeMarkup()}  ({row.File.Size:N0} B, {row.OutRefs} outRefs) - {row.Reason.EscapeMarkup()}");
        }
    }
}
