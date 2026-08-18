namespace JackAll.Core.Mods;

/// <summary>What a candidate mod folder/zip turns out to be, once its paths are run through the
/// very same resolution <see cref="IModLayer"/> applies when the builder reads it.</summary>
/// <param name="Root">The relative prefix the layer actually starts at, normalized (see
/// <see cref="Format.NameHash.Normalize"/>) and without a trailing separator — empty when the paths
/// are already rooted correctly. A community mod zip almost always wraps its tree in a folder named
/// after the mod, and that wrapper has to be stripped before anything hashes to the right entry.</param>
/// <param name="WholeFileOverrides">Paths that resolve to a standalone archive-entry override,
/// <c>_hash\</c>-addressed ones included.</param>
/// <param name="FragmentOverrides">Paths that resolve to one fragment inside a splitting `.fcb` — a
/// path-shaped id, possibly several levels deep. Two spellings of one fragment count once.</param>
/// <param name="HashAddressed">How many of the two counts above came in via <c>_hash\</c> rather
/// than a real relative path — informational, not a separate bucket.</param>
/// <param name="UnknownEntries">Overrides whose target isn't in the game's archives at all: either
/// files the mod genuinely adds, or the tell-tale of a root that was guessed wrong. Always 0 when
/// <see cref="ModLayerInspector.Inspect"/> was called without an <c>entryExists</c> probe.</param>
/// <param name="IgnoredFiles">Files outside the reserved <c>mods\</c>/<c>plugins\</c> folders
/// (readmes, screenshots — nothing else is layer content), plus the rare in-<c>mods\</c> path
/// <see cref="ModPathHashing.Resolve"/> rejects, such as a <c>_hash\</c> entry whose leaf isn't
/// hex.</param>
/// <param name="PluginFiles">Files under the reserved top-level <c>plugins\</c> folder — recognized
/// side-content the build deploys to <c>bin\plugins</c>, never compiled into patch.dat.</param>
public sealed record ModLayerReport(
    string Root,
    int WholeFileOverrides,
    int FragmentOverrides,
    int HashAddressed,
    int UnknownEntries,
    int IgnoredFiles,
    int PluginFiles,
    IReadOnlyList<string> SamplePaths)
{
    /// <summary>Everything this layer would contribute to a build.</summary>
    public int TotalOverrides => WholeFileOverrides + FragmentOverrides;

    /// <summary>Recognized contributions minus misses — what root scoring maximizes.</summary>
    public int RecognizedFiles => TotalOverrides - UnknownEntries + PluginFiles;
}

/// <summary>
/// Answers "is this pile of files a Far Cry 2 mod, and if so where does it start?" without building
/// a layer for it.
/// </summary>
/// <remarks>
/// This exists for callers that receive a mod as a bare directory of unknown shape — the Vortex
/// extension's installer, most immediately — and need the answer *before* anything is staged.
///
/// Root detection is the whole point. <see cref="ModPathHashing.Classify"/> only recognizes the
/// reserved <c>mods\</c>/<c>plugins\</c> folders at the top of a tree, so a zip that wraps them in
/// <c>MyCoolMod\</c> contributes nothing — silently, which is the worst way for this to fail.
/// Rather than guess from folder names, every plausible prefix is scored by how many of the files
/// below it classify as recognized content, and the best-scoring one wins. That reuses
/// <see cref="ModPathHashing"/> itself as the oracle, so this can never disagree with what the
/// builder will do later.
/// </remarks>
public static class ModLayerInspector
{
    /// <summary>How deep to look for the real root. Wrappers in the wild are one folder, occasionally
    /// two ("MyMod v1.2\MyMod\"); past this it's not a wrapper, it's the mod's own structure.</summary>
    public const int MaxRootDepth = 3;

    private const int SampleCount = 8;

    /// <summary>
    /// Scores every candidate root and returns the best one. <paramref name="entryExists"/> tells
    /// whether a hash is a real entry in the game's archives (<c>GameVfs</c>/a union of the
    /// <c>.fat</c> indices) — without it there's nothing to score against, so the root is assumed to
    /// be the top and the counts are reported as-is.
    /// </summary>
    public static ModLayerReport Inspect(
        IReadOnlyCollection<string> relativePaths, Func<uint, bool>? entryExists = null)
    {
        string[] normalized = [.. relativePaths.Select(Format.NameHash.Normalize).Where(p => p.Length > 0)];

        if (entryExists is null)
        {
            return Score(normalized, root: "", entryExists: null);
        }

        ModLayerReport best = Score(normalized, root: "", entryExists);
        foreach (string root in CandidateRoots(normalized))
        {
            ModLayerReport candidate = Score(normalized, root, entryExists);
            // Strictly better only: a deeper root has to actually recognize more files to win, so a
            // correctly-rooted mod is never "improved" into one of its own subfolders.
            if (candidate.RecognizedFiles > best.RecognizedFiles)
            {
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Every directory prefix, up to <see cref="MaxRootDepth"/> deep, that some file lives
    /// under — the only prefixes that could possibly be a wrapper.</summary>
    private static IEnumerable<string> CandidateRoots(IEnumerable<string> normalizedPaths)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in normalizedPaths)
        {
            // Stops at a container's own `.fcb` segment: everything below it is a fragment tree, never
            // a wrapper folder.
            string[] segments = path.Split('\\');
            int maxDepth = Math.Min(MaxRootDepth, segments.Length - 1);
            for (int depth = 1; depth <= maxDepth && !ModPathHashing.IsContainerSegment(segments[depth - 1]); depth++)
            {
                roots.Add(string.Join('\\', segments[..depth]));
            }
        }
        return roots;
    }

    private static ModLayerReport Score(
        IReadOnlyList<string> normalizedPaths, string root, Func<uint, bool>? entryExists)
    {
        string prefix = root.Length == 0 ? "" : root + "\\";
        int wholeFile = 0, fragments = 0, hashAddressed = 0, unknown = 0, ignored = 0, pluginFiles = 0;
        var samples = new List<string>(SampleCount);
        var seenFragments = new Dictionary<uint, HashSet<string>>();

        foreach (string path in normalizedPaths)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string relative = path[prefix.Length..];
            LayerPath classified = ModPathHashing.Classify(relative);
            if (classified.PluginPath is not null)
            {
                pluginFiles++;
                if (samples.Count < SampleCount)
                {
                    samples.Add(relative);
                }
                continue;
            }
            if (classified.Target is not { } target)
            {
                ignored++;
                continue;
            }

            if (target.ContainerHash is { } containerHash)
            {
                // Two spellings of one entity's id collapse into a single override at build time
                // (ModPathHashing.Add), so counting both here would over-report what a build merges.
                if (!seenFragments.TryGetValue(containerHash, out HashSet<string>? seenIds))
                {
                    seenIds = new HashSet<string>(Format.Fcb.FcbFragments.IdComparer);
                    seenFragments[containerHash] = seenIds;
                }
                if (!seenIds.Add(target.FragmentId!))
                {
                    continue;
                }

                fragments++;
                // A fragment's own EntryHash is a synthetic key for the staged file, never an archive
                // entry - it's the container that has to exist for this to mean anything.
                if (entryExists?.Invoke(containerHash) == false)
                {
                    unknown++;
                }
            }
            else
            {
                wholeFile++;
                if (entryExists?.Invoke(target.EntryHash) == false)
                {
                    unknown++;
                }
            }

            if (classified.ContentPath.StartsWith(ModPathHashing.HashFolder + "\\", StringComparison.Ordinal))
            {
                hashAddressed++;
            }
            if (samples.Count < SampleCount)
            {
                samples.Add(relative);
            }
        }

        return new ModLayerReport(root, wholeFile, fragments, hashAddressed, unknown, ignored, pluginFiles, samples);
    }
}
