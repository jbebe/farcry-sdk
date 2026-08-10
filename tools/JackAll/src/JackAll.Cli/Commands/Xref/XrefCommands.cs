using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Xrefs;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Xref;

/// <summary>
/// Where the reference index file lives, shared by every <c>xref</c> command.
/// </summary>
/// <remarks>
/// Defaults beside this exe rather than inside the game folder - nothing of JackAll's belongs in
/// there. JackAll.App keeps its own copy under its <c>data\</c> folder (see
/// <c>AppConfig.XrefFile</c>), so the two front ends do not share one by default; point
/// <c>--index</c> at the app's <c>data\.xrefs</c> to have a CLI build warm the app, or the other way
/// round. The file format is identical either way.
/// </remarks>
public class XrefFileSettings : GameCommandSettings
{
    [CommandOption("--index <file>")]
    [Description("Where to read/write the reference index (default: .xrefs beside this exe). "
               + "Point at JackAll.App's data\\.xrefs to share one index with the app.")]
    public string? IndexPath { get; init; }

    public string ResolveIndexPath()
        => string.IsNullOrWhiteSpace(IndexPath)
            ? Path.Combine(AppContext.BaseDirectory, ".xrefs")
            : Path.GetFullPath(IndexPath);
}

/// <summary>
/// Builds (or rebuilds) the hash reference graph for an install.
/// </summary>
/// <remarks>
/// The headless counterpart to JackAll.App's background indexing pass, and the only way to *measure*
/// it: the app hides the build behind a status line, while this prints the counts, timing and
/// resolution rate that decide whether the index is worth what it costs. Point <c>--index</c> at the
/// app's own <c>data\.xrefs</c> to have this warm the app's copy rather than keeping its own.
/// </remarks>
public sealed class XrefBuildCommand : CliCommand<XrefBuildCommand.Settings>
{
    public sealed class Settings : XrefFileSettings
    {
        [CommandOption("--rebuild")]
        [Description("Ignore any existing index and extract every file again.")]
        public bool Rebuild { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        GameInstall install = settings.OpenInstall();
        string indexPath = settings.ResolveIndexPath();
        var progress = new SyncProgress(JsonOutput.Report);

        NameDatabase names = BundledAssets.LoadNames();
        using GameVfs vfs = GameVfs.Load(
            install, names, GameCache.Load(install.CacheFile), BundledAssets.LoadFcbClasses(),
            progress, includeFragments: false);

        ReferenceIndex previous = settings.Rebuild
            ? ReferenceIndex.Empty
            : ReferenceIndex.Load(indexPath);

        var stopwatch = Stopwatch.StartNew();
        ReferenceBuildResult build = ReferenceIndexer.BuildBaseIndex(
            vfs, ReferenceExtractors.All, previous, progress, cancellationToken);
        stopwatch.Stop();
        ReferenceIndex index = build.Index;

        index.Save(indexPath);
        long fileSize = new FileInfo(indexPath).Length;

        // Resolution rate is the health check: a sudden drop means an extractor started emitting
        // paths the archives don't actually contain, which no unit test would notice.
        int filePathEdges = 0, resolved = 0, volatileFiles = 0;
        foreach (VfsFile file in vfs.Files.Values)
        {
            if (!vfs.IsStableSource(file))
            {
                volatileFiles++;
            }

            foreach (RefEdge edge in index.ReferencesFrom(file.Hash))
            {
                if (edge.TargetSpace != RefSpace.FilePath) continue;
                filePathEdges++;
                if (vfs.Files.ContainsKey(edge.Target)) resolved++;
            }
        }
        double rate = filePathEdges == 0 ? 0 : 100.0 * resolved / filePathEdges;

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                path = indexPath,
                edges = index.EdgeCount,
                definitions = index.DefinitionCount,
                indexedFiles = index.IndexedFileCount,
                filePathEdges,
                resolvedFilePathEdges = resolved,
                resolutionPercent = Math.Round(rate, 2),
                bytes = fileSize,
                seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
                failures = build.Failures.Select(f => new { path = f.Path, reason = f.Reason }).ToArray(),
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Indexed[/] {index.IndexedFileCount:N0} files in {stopwatch.Elapsed.TotalSeconds:N1}s");
        AnsiConsole.MarkupLine($"  edges            : {index.EdgeCount:N0}");
        AnsiConsole.MarkupLine($"  definitions      : {index.DefinitionCount:N0}");
        AnsiConsole.MarkupLine($"  file refs resolve: {resolved:N0}/{filePathEdges:N0} ({rate:N1}%)");
        AnsiConsole.MarkupLine($"  written to       : {indexPath.EscapeMarkup()} ({fileSize / 1024.0 / 1024.0:N1} MB)");
        // Not a warning - it's the design. Stated because "why is common.mgb missing?" is otherwise
        // a genuinely confusing five minutes.
        AnsiConsole.MarkupLine(
            $"  [grey]{volatileFiles:N0} file(s) come from patch.dat/mods and are indexed per session, not persisted[/]");

        if (build.Failures.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]{build.Failures.Count:N0} file(s) failed to decode[/]" +
                (build.Failures.Count >= ReferenceIndexer.MaxRecordedFailures ? " (list truncated)" : ""));
            foreach ((string failedPath, string reason) in build.Failures.Take(10))
            {
                AnsiConsole.MarkupLine($"    {failedPath.EscapeMarkup()} - {reason.EscapeMarkup()}");
            }
        }
        return 0;
    }
}

/// <summary>Shared options for the two query commands.</summary>
public class XrefQuerySettings : XrefFileSettings
{
    [CommandArgument(0, "<target>")]
    [Description("A game-relative path, or a hex hash (with or without 0x).")]
    public string Target { get; init; } = string.Empty;

    [CommandOption("--space <space>")]
    [Description("Which hash space a hex target is in: filepath (default), enginename, oasisstring, soundresource, deploadtype.")]
    public string Space { get; init; } = "filepath";

    /// <summary>
    /// Resolves the argument to (space, hash). A path is always a <see cref="RefSpace.FilePath"/>
    /// hash regardless of <c>--space</c> - hashing a path into any other space would be meaningless -
    /// so the option only ever applies to a bare hex value.
    /// </summary>
    public (RefSpace Space, uint Hash) Resolve()
    {
        string raw = Target.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Target[2..] : Target;
        bool looksHex = raw.Length is > 0 and <= 8
            && raw.All(Uri.IsHexDigit)
            && uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

        if (!looksHex)
        {
            return (RefSpace.FilePath, NameHash.Compute(Target));
        }

        uint hash = uint.Parse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (ParseSpace(Space), hash);
    }

    private static RefSpace ParseSpace(string text) => text.ToLowerInvariant() switch
    {
        "filepath" or "file" or "path" => RefSpace.FilePath,
        "enginename" or "name" => RefSpace.EngineName,
        "oasisstring" or "oasis" or "string" => RefSpace.OasisString,
        "soundresource" or "sound" => RefSpace.SoundResource,
        "deploadtype" or "deptype" => RefSpace.DepLoadType,
        _ => throw new InvalidOperationException(
            $"Unknown hash space '{text}'. Use one of: filepath, enginename, oasisstring, soundresource, deploadtype."),
    };
}

/// <summary>Lists everything that references a hash - the "who uses this?" direction.</summary>
public sealed class XrefToCommand : CliCommand<XrefQuerySettings>
{
    protected override int Run(XrefQuerySettings settings, CancellationToken cancellationToken)
        => XrefQuery.Run(settings, incoming: true);
}

/// <summary>Lists everything a file references.</summary>
public sealed class XrefFromCommand : CliCommand<XrefQuerySettings>
{
    protected override int Run(XrefQuerySettings settings, CancellationToken cancellationToken)
        => XrefQuery.Run(settings, incoming: false);
}

internal static class XrefQuery
{
    public static int Run(XrefQuerySettings settings, bool incoming)
    {
        // Validates --game (so a typo is reported as such) even though a query only reads the index -
        // the index is meaningless without knowing which install it describes.
        settings.OpenInstall();
        string indexPath = settings.ResolveIndexPath();

        ReferenceIndex index = ReferenceIndex.Load(indexPath);
        if (index.EdgeCount == 0)
        {
            throw new InvalidOperationException(
                $"No reference index at {indexPath}. Run 'jackall-cli xref build --game ...' first.");
        }

        NameDatabase names = BundledAssets.LoadNames();
        (RefSpace space, uint hash) = settings.Resolve();

        IReadOnlyList<RefEdge> edges = incoming
            ? index.ReferencesTo(space, hash)
            : index.ReferencesFrom(hash);

        var rows = edges.Select(edge => new
        {
            source = Describe(names, edge.SourceFile),
            target = space == RefSpace.FilePath && !incoming
                ? Describe(names, edge.Target)
                : $"{edge.TargetSpace}:{edge.Target:X8}",
            targetSpace = edge.TargetSpace.ToString(),
            site = DescribeSite(index, names, edge),
            kind = edge.Kind.ToString(),
        }).ToArray();

        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                space = space.ToString(),
                hash = hash.ToString("X8"),
                count = rows.Length,
                inBaseIndex = index.IsIndexed(hash),
                edges = rows,
            });
            return 0;
        }

        if (rows.Length == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No references[/] {(incoming ? "to" : "from")} {space}:{hash:X8}");

            // A file the index never visited and a file with genuinely no references both show up as
            // an empty list, and they mean completely different things. The persisted index covers
            // base archives only - anything patch.dat, a mod or the workspace supplies is merged in
            // at runtime by the app and is simply absent here.
            if (!incoming && !index.IsIndexed(hash))
            {
                AnsiConsole.MarkupLine(
                    "[grey]This file isn't in the base index at all - it's supplied by patch.dat, a mod, or the " +
                    "workspace, whose references JackAll.App merges in per session rather than persisting.[/]");
            }
            return 0;
        }

        var table = new Table().Border(TableBorder.Simple);
        table.AddColumn(incoming ? "referenced by" : "references");
        table.AddColumn("site");
        table.AddColumn("kind");
        foreach (var row in rows)
        {
            table.AddRow(
                (incoming ? row.source : row.target).EscapeMarkup(),
                row.site.EscapeMarkup(),
                row.kind);
        }
        AnsiConsole.Write(table);
        return 0;
    }

    /// <summary>A file's recovered path, or its bare hash when the filelist never named it - the same
    /// "usable but unnamed" treatment the rest of the tool gives those entries.</summary>
    private static string Describe(NameDatabase names, uint hash)
        => names.TryResolve(hash, out string path) ? path : $"#{hash:X8}";

    /// <summary>Mirrors the app's own site rendering (see <c>MainViewModel.DescribeSite</c>) so the
    /// two front ends can't disagree about what a row says: a `depload.dat` sites its edges by the
    /// parent resource's *file* hash, everything else by a name the index's table can resolve.</summary>
    private static string DescribeSite(ReferenceIndex index, NameDatabase names, RefEdge edge)
    {
        if (RefKinds.SiteIsFileHash(edge.Kind))
        {
            return Describe(names, edge.SiteKey);
        }

        string? name = index.Name(edge.SiteKey);
        if (name is null)
        {
            return $"#{edge.SiteKey:X8}";
        }
        return edge.SiteIndex == 0 ? name : $"{name}[{edge.SiteIndex}]";
    }
}
