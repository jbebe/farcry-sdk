using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// The bits of layer/VFS plumbing more than one <c>mod</c> command needs, kept together so they
/// can't drift apart — same reasoning as <see cref="CliIO"/> and <see cref="CliAssets"/>.
/// </summary>
internal static class ModLayerLoading
{
    /// <summary>
    /// Opens one <c>--layer</c> argument. A zip and a folder are the same thing to everything
    /// downstream (see <see cref="IModLayer"/>), so the only decision here is which reader to use.
    /// A path that doesn't exist is a hard error rather than an empty layer: a mistyped path would
    /// otherwise produce a perfectly successful build that quietly left a mod out.
    /// </summary>
    public static IModLayer Open(string path)
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
    /// Mounts the game's archives so <see cref="GameVfs.ReadOriginal"/> is available. Neither CLI
    /// caller ever browses the merged file index or fragment tree — both only ever call
    /// <c>ReadOriginal</c>/<c>ReadOriginalHash</c> — so this goes through
    /// <see cref="GameVfs.OpenForOriginalsOnly"/> rather than <see cref="GameVfs.Load"/>, skipping
    /// the <c>BuildMergedFiles</c> pass entirely instead of merely trimming it (its old
    /// <c>includeFragments: false</c>, which only skipped the `.fcb` fragment half of that pass).
    /// </summary>
    /// <remarks>
    /// Every CLI invocation is a fresh process with nothing warm to reuse, unlike JackAll.App (one
    /// long-lived session, one in-memory <see cref="GameCache"/> for its whole lifetime) - so without
    /// a persistent cache, every single <c>mod build</c>/<c>mod import-legacy</c> run would re-hash
    /// every vanilla entry <see cref="GameVfs.ReadOriginalHash"/> is asked for from scratch. Loading
    /// <see cref="GameInstall.CacheFile"/> here instead of an empty <see cref="GameCache"/> means only
    /// the *first* CLI run against a given install pays that cost; every later one reads it back from
    /// disk in one gulp. Saving it back is the caller's job (see <see cref="GameVfs.Cache"/>'s
    /// remarks) - this method only loads.
    /// </remarks>
    public static GameVfs LoadVfs(GameInstall install, NameDatabase names)
        => GameVfs.OpenForOriginalsOnly(
            install,
            names,
            cache: GameCache.Load(install.CacheFile),
            progress: new ImmediateProgress(JsonOutput.Report));

    /// <summary>
    /// Every entry hash the *base game* has, read straight off the <c>.fat</c> indices — far cheaper
    /// than mounting a <see cref="GameVfs"/> when all a caller needs is "does this hash exist".
    /// </summary>
    /// <remarks>
    /// The live <c>patch.fat</c> is skipped once a vanilla backup exists, and the backup read in its
    /// place: after even one deploy the live patch is JackAll's own build output, and counting a
    /// previous mod's added entries as "files the game has" would let
    /// <see cref="ModLayerInspector"/> score a wrong root as plausible.
    /// </remarks>
    public static HashSet<uint> ReadBaseGameHashes(GameInstall install)
    {
        var hashes = new HashSet<uint>();
        bool skipLivePatch = install.HasVanillaBackup;

        foreach (string fat in install.EnumerateArchiveFats())
        {
            if (skipLivePatch && string.Equals(fat, install.PatchFat, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            AddEntries(hashes, fat);
        }

        if (skipLivePatch)
        {
            AddEntries(hashes, install.VanillaPatchFat);
        }
        return hashes;
    }

    private static void AddEntries(HashSet<uint> hashes, string fatPath)
    {
        try
        {
            foreach (FatEntry entry in FatArchive.Read(fatPath).Entries)
            {
                hashes.Add(entry.Hash);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            // One unreadable index makes the answer incomplete, not wrong - the remaining archives
            // still tell us what they hold, and this is only ever used for scoring.
            JsonOutput.Report($"Skipped unreadable index {fatPath}: {ex.Message}");
        }
    }
}
