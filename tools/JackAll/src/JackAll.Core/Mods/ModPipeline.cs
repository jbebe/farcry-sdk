using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Mods;

/// <summary>
/// The plumbing a headless mod build needs, in one place so the two CLIs can't drift apart:
/// turning a <c>--layer</c> path into an <see cref="IModLayer"/>, mounting the game's archives for
/// the vanilla originals a fragment merge is resolved against, and the build itself.
/// </summary>
public static class ModPipeline
{
    /// <summary>
    /// A zip and a folder are the same thing to everything downstream (see <see cref="IModLayer"/>),
    /// so the only decision here is which reader to use. A path that doesn't exist is a hard error
    /// rather than an empty layer: a mistyped path would otherwise produce a perfectly successful
    /// build that quietly left a mod out.
    /// </summary>
    public static IModLayer OpenLayer(string path)
    {
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new ZipModLayer(path);
        }
        if (Directory.Exists(path))
        {
            return new FolderModLayer(path, DefaultLayerName(path));
        }
        throw new DirectoryNotFoundException($"Layer not found: {path}");
    }

    /// <summary>True for a zip file, false for a folder — the two shapes a mod source can take.
    /// Anything else throws, with the message every front end already shows for it.</summary>
    public static bool IsZipSource(string path)
    {
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (Directory.Exists(path))
        {
            return false;
        }
        throw new FileNotFoundException($"Not a folder or .zip: {path}");
    }

    /// <summary>A layer's display name when nothing better was given: its folder or file name.</summary>
    public static string DefaultLayerName(string path)
        => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

    /// <summary>
    /// The whole headless build: mounts the archives only when some layer overrides part of an
    /// `.fcb` (both as the base to splice onto and as the merge ancestor — mounting costs seconds),
    /// builds with load-order conflict resolution (nobody is present to hand-fix a collision the way
    /// JackAll.App's conflict row allows), and persists whatever the run freshly sniffed or hashed.
    /// </summary>
    public static BuildResult Build(GameInstall install, IReadOnlyList<IModLayer> layers, IProgress<string> progress)
    {
        bool needsOriginals = layers.Any(l => l.Enabled && l.FragmentOverrides.Count > 0);

        GameVfs? vfs = null;
        try
        {
            FcbClassDefinitions? definitions = null;
            Func<uint, byte[]?>? readOriginal = null;
            if (needsOriginals)
            {
                progress.Report("A mod overrides part of an .fcb - mounting the game's archives for the originals…");
                definitions = BundledAssets.LoadFcbClasses();
                vfs = OpenOriginals(install, BundledAssets.LoadNames(), progress);
                readOriginal = vfs.ReadOriginal;
            }

            progress.Report($"Building patch.dat from {layers.Count} layer(s)…");
            return PatchBuilder.Build(install, layers, readOriginal, definitions,
                resolveFragmentConflictsWithLoadOrder: true);
        }
        finally
        {
            // The caller's job normally (see GameVfs.Cache), but this method owns the vfs it opened.
            // Saved before Dispose closes the archive handles, though the cache doesn't depend on them.
            if (vfs is not null)
            {
                SaveCache(vfs, install);
                vfs.Dispose();
            }
        }
    }

    /// <summary>Persists what a run freshly sniffed/decoded/hashed, so the next CLI invocation
    /// against this install doesn't pay for it again — see <see cref="GameVfs.Cache"/>'s remarks on
    /// why saving is the opener's job.</summary>
    public static void SaveCache(GameVfs vfs, GameInstall install)
    {
        if (vfs.Cache.IsDirty)
        {
            vfs.Cache.Save(install.CacheFile);
        }
    }

    /// <summary>
    /// Mounts the game's archives so <see cref="GameVfs.ReadOriginal"/> is available. No CLI caller
    /// ever browses the merged file index or fragment tree — they only call
    /// <c>ReadOriginal</c>/<c>ReadOriginalHash</c> — so this goes through
    /// <see cref="GameVfs.OpenForOriginalsOnly"/> rather than <see cref="GameVfs.Load"/>, skipping the
    /// <c>BuildMergedFiles</c> pass nothing here would read.
    /// </summary>
    /// <remarks>
    /// Every CLI invocation is a fresh process with nothing warm to reuse, unlike JackAll.App (one
    /// long-lived session, one in-memory <see cref="GameCache"/> for its whole lifetime) - so without
    /// a persistent cache, every <c>mod build</c>/<c>mod import-legacy</c> run would re-hash every
    /// vanilla entry <see cref="GameVfs.ReadOriginalHash"/> is asked for from scratch. Loading
    /// <see cref="GameInstall.CacheFile"/> here instead of an empty <see cref="GameCache"/> means only
    /// the *first* run against a given install pays that cost; every later one reads it back from disk
    /// in one gulp. Saving it back is the caller's job (see <see cref="GameVfs.Cache"/>'s remarks) -
    /// this method only loads.
    /// </remarks>
    public static GameVfs OpenOriginals(GameInstall install, NameDatabase names, IProgress<string>? progress = null)
        => GameVfs.OpenForOriginalsOnly(install, names, cache: GameCache.Load(install.CacheFile), progress: progress);
}
