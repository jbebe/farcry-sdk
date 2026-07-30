using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Cli.Commands.Mod;

/// <summary>
/// Layer/VFS plumbing specific to this CLI. The parts both CLIs need - opening a layer, mounting the
/// archives for originals - live in <see cref="ModPipeline"/> instead.
/// </summary>
internal static class ModLayerLoading
{
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
