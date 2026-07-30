using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Mods;

/// <summary>
/// The plumbing a headless mod build needs, in one place so the two CLIs can't drift apart:
/// turning a <c>--layer</c> path into an <see cref="IModLayer"/>, and mounting the game's archives
/// for the vanilla originals a fragment merge is resolved against.
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
            return new FolderModLayer(path, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))));
        }
        throw new DirectoryNotFoundException($"Layer not found: {path}");
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
