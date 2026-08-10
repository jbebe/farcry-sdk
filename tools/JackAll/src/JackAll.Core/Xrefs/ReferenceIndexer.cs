using System.Collections.Concurrent;
using JackAll.Core.Vfs;

namespace JackAll.Core.Xrefs;

/// <summary>What one extraction pass produced, before it's laid out into an index.</summary>
/// <param name="Failures">
/// Files whose extractor threw, with the reason - one entry per file, capped at
/// <see cref="ReferenceIndexer.MaxRecordedFailures"/>. The build deliberately carries on past a file
/// it can't decode, which means a broken extractor otherwise shows up as *silence* rather than an
/// error. Reporting the count is what turns "this panel is empty" back into a question with an
/// answer.
/// </param>
public sealed record ReferenceHarvest(
    IReadOnlyList<RefEdge> Edges,
    IReadOnlyList<RefDefinition> Definitions,
    IReadOnlyDictionary<uint, string> Names,
    IReadOnlyList<uint> Files,
    IReadOnlyList<(string Path, string Reason)> Failures);

/// <summary>A completed base-index build: the index itself, plus whatever refused to decode on the
/// way (see <see cref="ReferenceHarvest.Failures"/>).</summary>
public sealed record ReferenceBuildResult(
    ReferenceIndex Index,
    IReadOnlyList<(string Path, string Reason)> Failures);

/// <summary>
/// Runs every <see cref="IReferenceExtractor"/> across the merged filesystem and lays the result out
/// into a <see cref="ReferenceIndex"/>.
/// </summary>
/// <remarks>
/// Split into two passes on purpose, because the two have completely different lifetimes:
/// <see cref="BuildBaseIndex"/> covers files whose winning bytes come from a base archive - those
/// never change, so its output is persisted and reused across launches - while
/// <see cref="HarvestOverlay"/> covers files supplied by a mod, the workspace, or the tool's own
/// `patch.dat`, which have to be re-read every session and after every mod toggle. Merging the two
/// is <see cref="ReferenceGraph"/>'s job. Doing it in one pass would mean either persisting
/// mod-specific edges (wrong the moment a mod is disabled) or re-extracting all ~180,000 files on
/// every toggle (minutes, every time).
///
/// The extractor set is passed in rather than constructed here: the `.mgb`/`.spk`/`.xbm`/`.xbg`
/// decoders live in <c>JackAll.Tools</c>, which references this project rather than the other way
/// round, so Core can't name them.
/// </remarks>
public static class ReferenceIndexer
{
    /// <summary>
    /// Extracts references from every base-archive file not already covered by
    /// <paramref name="previous"/>, and returns an index holding both those and the reusable ones.
    /// </summary>
    /// <remarks>
    /// Reads and decodes fan out across cores, exactly like <see cref="GameVfs.LoadFragments"/>'s own
    /// decode pass and for the same reasons: every file is independent, and archive reads are
    /// lock-free (see <see cref="Format.DuniaArchive"/>). Each worker fills its own sink, so nothing
    /// is shared until the merge below - which also means the site-name vocabularies have to be
    /// unioned rather than assumed identical.
    /// </remarks>
    public static ReferenceBuildResult BuildBaseIndex(
        GameVfs vfs,
        IReadOnlyList<IReferenceExtractor> extractors,
        ReferenceIndex previous,
        IProgress<string>? progress = null,
        CancellationToken cancellation = default)
    {
        var todo = new List<VfsFile>();
        var reusable = new List<uint>();

        foreach (VfsFile file in vfs.Files.Values)
        {
            if (file.IsFragment || file.IsDependencyLink || !vfs.IsStableSource(file))
            {
                continue;
            }

            if (previous.IsIndexed(file.Hash))
            {
                reusable.Add(file.Hash);
            }
            else if (Extractor(extractors, file) is not null)
            {
                todo.Add(file);
            }
            else
            {
                // Nothing can extract from this type. Still recorded as indexed, so a later launch
                // doesn't reconsider it - "no extractor" is a stable fact about the file's type.
                reusable.Add(file.Hash);
            }
        }

        progress?.Report($"Indexing references in {todo.Count:N0} files…");
        ReferenceHarvest harvest = Harvest(vfs, extractors, todo, progress, cancellation);

        // Reused edges come straight back out of the previous index rather than being re-extracted.
        var edges = new List<RefEdge>(harvest.Edges);
        var definitions = new List<RefDefinition>(harvest.Definitions);

        // The previous name table comes over wholesale rather than being reconstructed from the
        // reused edges' site keys: a name can also belong to an EngineName *target* (an `.xbg`
        // material name is one), which no walk over site keys would ever find. The table is tens of
        // thousands of entries against millions of edges, so keeping all of it is free.
        var names = new Dictionary<uint, string>(previous.AllNames());
        foreach ((uint key, string name) in harvest.Names)
        {
            names[key] = name;
        }

        foreach (uint hash in reusable)
        {
            edges.AddRange(previous.ReferencesFrom(hash));
        }
        CarryOverPreviousDefinitions(previous, reusable, definitions);

        return new ReferenceBuildResult(
            ReferenceIndex.Build(edges, definitions, names, reusable.Concat(harvest.Files)),
            harvest.Failures);
    }

    /// <summary>
    /// Extracts references from everything the base index deliberately left out - mod layers, the
    /// workspace, and the tool's own `patch.dat`. Cheap by construction: a mod is tens to hundreds of
    /// files, not the whole game.
    /// </summary>
    public static ReferenceHarvest HarvestOverlay(
        GameVfs vfs,
        IReadOnlyList<IReferenceExtractor> extractors,
        CancellationToken cancellation = default)
    {
        var todo = vfs.Files.Values
            .Where(f => !f.IsFragment && !f.IsDependencyLink && !vfs.IsStableSource(f))
            .Where(f => Extractor(extractors, f) is not null)
            .ToList();

        return Harvest(vfs, extractors, todo, progress: null, cancellation);
    }

    private static ReferenceHarvest Harvest(
        GameVfs vfs,
        IReadOnlyList<IReferenceExtractor> extractors,
        IReadOnlyList<VfsFile> files,
        IProgress<string>? progress,
        CancellationToken cancellation)
    {
        if (files.Count == 0)
        {
            return new ReferenceHarvest([], [], new Dictionary<uint, string>(), [], []);
        }

        var results = new ConcurrentBag<(RefEdge[] Edges, RefDefinition[] Definitions, Dictionary<uint, string> Names)>();
        var failures = new ConcurrentQueue<(string Path, string Reason)>();
        int done = 0;

        Parallel.ForEach(
            files,
            new ParallelOptions { CancellationToken = cancellation },
            // One sink per worker, reused across every file that worker handles - the alternative
            // (a sink per file) would reallocate three collections ~180,000 times.
            () => new ReferenceSink(vfs.Definitions),
            (file, _, sink) =>
            {
                IReferenceExtractor? extractor = Extractor(extractors, file);
                if (extractor is not null)
                {
                    sink.BeginFile(file.Hash);
                    try
                    {
                        extractor.Extract(file, vfs.Read(file.Hash), sink);
                    }
                    catch (Exception ex)
                    {
                        // A file that won't decode has no discoverable references - the same
                        // tolerance the app's file handlers already apply. Failing the whole index
                        // over one malformed entry would be a far worse trade. It is still recorded:
                        // a *systematically* failing extractor and a single corrupt entry look
                        // identical from here otherwise.
                        if (failures.Count < MaxRecordedFailures)
                        {
                            failures.Enqueue((file.Path, $"{ex.GetType().Name}: {ex.Message}"));
                        }
                    }
                }

                int count = Interlocked.Increment(ref done);
                if (count % 2000 == 0)
                {
                    progress?.Report($"Indexing references… {count:N0}/{files.Count:N0}");
                }
                return sink;
            },
            sink =>
            {
                (RefEdge[] edges, RefDefinition[] definitions) = sink.Drain();
                results.Add((edges, definitions, new Dictionary<uint, string>(sink.Names)));
            });

        var allEdges = new List<RefEdge>();
        var allDefinitions = new List<RefDefinition>();
        var allNames = new Dictionary<uint, string>();
        foreach ((RefEdge[] edges, RefDefinition[] definitions, Dictionary<uint, string> names) in results)
        {
            allEdges.AddRange(edges);
            allDefinitions.AddRange(definitions);
            foreach ((uint key, string name) in names)
            {
                allNames.TryAdd(key, name);
            }
        }

        return new ReferenceHarvest(
            allEdges, allDefinitions, allNames, [.. files.Select(f => f.Hash)], [.. failures]);
    }

    /// <summary>Enough failures to tell a systematic problem from a one-off, without letting a
    /// catastrophically wrong extractor allocate a string per file across the whole game.</summary>
    public const int MaxRecordedFailures = 200;

    /// <summary>The first extractor claiming <paramref name="file"/>, or null when none does - also
    /// the "is this file worth reading at all" test, so an unclaimed type is never decompressed.</summary>
    private static IReferenceExtractor? Extractor(IReadOnlyList<IReferenceExtractor> extractors, VfsFile file)
    {
        for (int i = 0; i < extractors.Count; i++)
        {
            if (extractors[i].CanHandle(file))
            {
                return extractors[i];
            }
        }
        return null;
    }

    /// <summary>Definitions are keyed by (space, id), not by file, so they can't be looked up per
    /// reused file the way edges can - they're carried over by scanning the previous index for
    /// definitions whose defining file is one being reused.</summary>
    private static void CarryOverPreviousDefinitions(
        ReferenceIndex previous, List<uint> reusedFiles, List<RefDefinition> definitions)
    {
        if (reusedFiles.Count == 0)
        {
            return;
        }

        var reused = new HashSet<uint>(reusedFiles);
        foreach (RefDefinition definition in previous.AllDefinitions())
        {
            if (reused.Contains(definition.DefiningFile))
            {
                definitions.Add(definition);
            }
        }
    }
}
