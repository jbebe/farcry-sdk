using JackAll.Core.Format;
using JackAll.Core.Xrefs;

namespace JackAll.Tools.Reach;

/// <summary>One corpus file as the analysis sees it - a snapshot, so the engine stays testable
/// without a mounted VFS.</summary>
public sealed record ReachFile(
    uint Hash, string Path, string Extension, long Size, string SourceName, bool NameIsKnown);

public enum ReachVerdict
{
    Used,
    UsedSpOnly,
    UsedMpOnly,
    Unused,
    Unknown,
}

public sealed record ReachRow(
    ReachFile File, ReachVerdict Verdict, ReachFlags Flags, int OutRefs, string Reason);

public sealed record ReachResult(
    IReadOnlyList<ReachRow> Rows,
    IReadOnlyList<string> Warnings,
    int SeededFiles,
    int NameProbeMatches)
{
    /// <summary>hash → the file that first reached it, for rendering a reach chain.</summary>
    public required IReadOnlyDictionary<uint, uint> Predecessors { get; init; }
}

/// <summary>
/// The reachability closure: seeds every root the engine can name, propagates SP/MP/Editor flags
/// through the reference graph to a fixpoint, then turns flags into verdicts under
/// <see cref="ReachPolicy"/>'s conservative rules.
/// </summary>
public static class ReachAnalysis
{
    public static ReachResult Run(IReadOnlyList<ReachFile> corpus, ReferenceGraph graph, EngineRoots roots)
    {
        if (graph.BaseEdgeCount + graph.OverlayEdgeCount == 0)
        {
            throw new InvalidOperationException(
                "The reference index is empty - an empty index would mark every file unused. Build it first (xref build, or --build).");
        }

        var byHash = new Dictionary<uint, ReachFile>(corpus.Count);
        foreach (ReachFile file in corpus)
        {
            byHash[file.Hash] = file;
        }

        var flags = new Dictionary<uint, ReachFlags>();
        var reason = new Dictionary<uint, string>();
        var predecessor = new Dictionary<uint, uint>();
        var queue = new Queue<uint>();
        var warnings = new List<string>();
        int nameProbes = 0;

        bool Raise(uint hash, ReachFlags add, string why, uint from)
        {
            add = add.Modes();
            ReachFlags old = flags.GetValueOrDefault(hash);
            if (add == ReachFlags.None || (old | add) == old)
            {
                return false;
            }
            flags[hash] = old | add;
            if (reason.TryAdd(hash, why))
            {
                predecessor[hash] = from;
            }
            queue.Enqueue(hash);
            return true;
        }

        string PathOf(uint hash)
            => byHash.TryGetValue(hash, out ReachFile? f) ? f.Path : $"#{hash:X8}";

        // ---- seeds + per-file root annotations
        var suppressed = new Dictionary<uint, string>();
        var fallback = new Dictionary<uint, string>();
        var unknownTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ReachFile file in corpus)
        {
            RootMatch match = roots.Match(file.Path, file.SourceName);
            if (match.UnknownWorldTokens is not null)
            {
                unknownTokens.UnionWith(match.UnknownWorldTokens);
            }
            if (match.SuppressedReason is not null)
            {
                suppressed.TryAdd(file.Hash, match.SuppressedReason);
            }
            if (match.FallbackReason is not null)
            {
                fallback.TryAdd(file.Hash, match.FallbackReason);
            }
            // Console leftovers are excluded from seeding outright; a followed edge can still
            // raise them, and the verdict override below has the final word either way.
            if (match.Flags != ReachFlags.None && !ReachPolicy.IsConsoleOnly(file.Path))
            {
                Raise(file.Hash, match.Flags, match.Reason!, 0);
            }
        }
        int seeded = flags.Count;
        warnings.AddRange(unknownTokens.Order().Select(t => $"world token '{t}' matches no world rule"));

        // ---- one sweep over the whole graph for the lookups the BFS needs
        var deploadByManifest = new Dictionary<uint, List<(uint Parent, uint Child)>>();
        var deploadByParent = new Dictionary<uint, List<(uint Manifest, uint Child)>>();
        foreach (RefEdge edge in graph.AllEdges())
        {
            if (edge.Kind != RefKind.DepLoadDependency)
            {
                continue;
            }
            if (!deploadByManifest.TryGetValue(edge.SourceFile, out var children))
            {
                deploadByManifest[edge.SourceFile] = children = [];
            }
            children.Add((edge.SiteKey, edge.Target));
            if (!deploadByParent.TryGetValue(edge.SiteKey, out var asParent))
            {
                deploadByParent[edge.SiteKey] = asParent = [];
            }
            asParent.Add((edge.SourceFile, edge.Target));
        }

        var definers = new Dictionary<(RefSpace, uint), List<uint>>();
        foreach (RefDefinition definition in graph.AllDefinitions())
        {
            if (!definers.TryGetValue((definition.Space, definition.Id), out var files))
            {
                definers[(definition.Space, definition.Id)] = files = [];
            }
            files.Add(definition.DefiningFile);
        }

        var nameFlags = new Dictionary<(RefSpace, uint), ReachFlags>();

        void RaiseName(RefSpace space, uint id, ReachFlags add, uint from)
        {
            ReachFlags old = nameFlags.GetValueOrDefault((space, id));
            if ((old | add) == old)
            {
                return;
            }
            nameFlags[(space, id)] = old | add;
            if (definers.TryGetValue((space, id), out var files))
            {
                foreach (uint file in files)
                {
                    Raise(file, old | add, $"via:definition:{PathOf(from)}", from);
                }
            }
        }

        // ---- fixpoint
        while (queue.TryDequeue(out uint source))
        {
            ReachFlags f = flags[source];

            foreach (RefEdge edge in graph.ReferencesFrom(source))
            {
                if (ReachPolicy.PropagatesToFile(edge.Kind))
                {
                    Raise(edge.Target, f, $"via:{edge.Kind}:{PathOf(source)}", source);
                }
                else if (ReachPolicy.PropagatesToName(edge.Kind))
                {
                    RaiseName(edge.TargetSpace, edge.Target, f, source);

                    // Name-probe: an .fcb Hash member holding what is really a path hash (landmark
                    // vegetation resource lists do exactly this). A numeric coincidence yields a
                    // false `used` - the acceptable direction; the count keeps a blow-up visible.
                    if (edge.TargetSpace == RefSpace.EngineName && byHash.ContainsKey(edge.Target)
                        && Raise(edge.Target, f, $"via:name-probe:{PathOf(source)}", source))
                    {
                        nameProbes++;
                    }

                    // Sound ids resolve to files by the engine's own %08x naming rule.
                    if (edge.TargetSpace == RefSpace.SoundResource)
                    {
                        uint sbao = NameHash.Compute($@"soundbinary\{edge.Target:x8}.sbao");
                        uint bao = NameHash.Compute($@"soundbinary\{edge.Target:x8}.bao");
                        if (byHash.ContainsKey(sbao))
                        {
                            Raise(sbao, f, $"via:sound-id:{PathOf(source)}", source);
                        }
                        if (byHash.ContainsKey(bao))
                        {
                            Raise(bao, f, $"via:sound-id:{PathOf(source)}", source);
                        }
                    }
                }
            }

            // Depload is doubly gated: a child loads only when its manifest's world is live AND its
            // parent resource is itself reachable. The child inherits the *manifest's* flags - the
            // manifest listing the pair is the engine's own statement of which world pulls it in.
            if (deploadByManifest.TryGetValue(source, out var children))
            {
                foreach ((uint parent, uint child) in children)
                {
                    if (flags.GetValueOrDefault(parent) != ReachFlags.None)
                    {
                        Raise(child, f, $"via:depload:{PathOf(parent)}", source);
                    }
                }
            }
            if (deploadByParent.TryGetValue(source, out var asParent))
            {
                foreach ((uint manifest, uint child) in asParent)
                {
                    ReachFlags manifestFlags = flags.GetValueOrDefault(manifest);
                    if (manifestFlags != ReachFlags.None)
                    {
                        Raise(child, manifestFlags, $"via:depload:{PathOf(source)}", manifest);
                    }
                }
            }
        }

        foreach (RootRule rule in roots.UnmatchedRules())
        {
            warnings.Add($"root rule at line {rule.LineNumber} matched nothing: {rule.Kind} {rule.Value}");
        }

        // ---- verdicts
        var rows = new List<ReachRow>(corpus.Count);
        foreach (ReachFile file in corpus)
        {
            ReachFlags f = flags.GetValueOrDefault(file.Hash).Modes();
            int outRefs = graph.ReferencesFrom(file.Hash).Count;
            ReachVerdict verdict;
            string why;
            bool knownDead = false;

            if (ReachPolicy.KnownCollisions.TryGetValue(file.Hash, out string? twin))
            {
                verdict = f == ReachFlags.None ? ReachVerdict.Unknown : FromFlags(f);
                why = $"collision:{twin}";
            }
            else if (ReachPolicy.IsConsoleOnly(file.Path))
            {
                verdict = ReachVerdict.Unused;
                f = ReachFlags.None;
                why = "console-only";
            }
            else if (fallback.TryGetValue(file.Hash, out string? fallbackWhy))
            {
                // A curated override, like console-only: fallback rules carry RE-verified "the
                // engine never reads this" facts, and those outrank a followed reference (the
                // engine config names movemgrnamed.bin in a slot retail code never consumes).
                knownDead = true;
                verdict = ReachVerdict.Unused;
                f = ReachFlags.None;
                why = fallbackWhy;
            }
            else if (f != ReachFlags.None)
            {
                verdict = FromFlags(f);
                why = reason[file.Hash];
            }
            else if (suppressed.TryGetValue(file.Hash, out string? suppressedWhy))
            {
                verdict = ReachVerdict.Unused;
                why = suppressedWhy;
            }
            else if (!file.NameIsKnown)
            {
                verdict = ReachVerdict.Unknown;
                why = "unnamed";
            }
            else if (ReachPolicy.IsOpaquePath(file.Path))
            {
                verdict = ReachVerdict.Unknown;
                why = "opaque-referrers(domino)";
            }
            else if (ReachPolicy.OpaqueReferrerExtensions.Contains(file.Extension))
            {
                verdict = ReachVerdict.Unknown;
                why = $"opaque-referrers({file.Extension})";
            }
            else
            {
                verdict = ReachVerdict.Unused;
                why = "unreachable";
            }

            // A name the engine knows but never reads is the decoy shape by definition; otherwise
            // size or reference count has to make the file look load-bearing.
            if (verdict == ReachVerdict.Unused
                && (knownDead || outRefs >= ReachPolicy.DecoyOutRefs || file.Size >= ReachPolicy.DecoyBytes))
            {
                f |= ReachFlags.Decoy;
            }

            rows.Add(new ReachRow(file, verdict, f, outRefs, why));
        }

        rows.Sort((a, b) => a.File.NameIsKnown != b.File.NameIsKnown
            ? (a.File.NameIsKnown ? -1 : 1)
            : string.CompareOrdinal(a.File.Path, b.File.Path));

        return new ReachResult(rows, warnings, seeded, nameProbes) { Predecessors = predecessor };
    }

    private static ReachVerdict FromFlags(ReachFlags flags)
    {
        bool sp = flags.HasFlag(ReachFlags.SP);
        bool mp = flags.HasFlag(ReachFlags.MP);
        if (sp && mp)
        {
            return ReachVerdict.Used;
        }
        if (sp)
        {
            return ReachVerdict.UsedSpOnly;
        }
        // Editor-only stays `used` - the map editor ships with the PC game; the flags column
        // preserves the distinction for consumers that want to trim it.
        return mp ? ReachVerdict.UsedMpOnly : ReachVerdict.Used;
    }
}
