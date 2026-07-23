namespace JackAll.Core;

/// <summary>
/// A validated Far Cry 2 installation, and the custodian of the pristine patch backup.
/// </summary>
/// <remarks>
/// The build always regenerates patch.dat/.fat from the vanilla backup, never from whatever is
/// currently on disk. That single rule is what makes builds idempotent and "remove the mod, rebuild"
/// a true revert — without it, each build would layer on top of the last and there would be no way
/// back to a clean game short of reinstalling.
/// </remarks>
public sealed class GameInstall
{
    public const string VanillaSuffix = ".vanilla";

    public string RootPath { get; }
    public string DataDir => Path.Combine(RootPath, "Data_Win32");
    public string PatchFat => Path.Combine(DataDir, "patch.fat");
    public string PatchDat => Path.Combine(DataDir, "patch.dat");
    public string VanillaPatchFat => PatchFat + VanillaSuffix;
    public string VanillaPatchDat => PatchDat + VanillaSuffix;

    public bool HasVanillaBackup => File.Exists(VanillaPatchFat) && File.Exists(VanillaPatchDat);

    private GameInstall(string rootPath) => RootPath = rootPath;

    /// <summary>Validates a candidate folder. Returns null with a reason when it isn't an FC2 install.</summary>
    public static GameInstall? TryOpen(string rootPath, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            error = "That folder doesn't exist.";
            return null;
        }

        var install = new GameInstall(rootPath);

        if (!File.Exists(Path.Combine(rootPath, "bin", "FarCry2.exe")))
        {
            error = "No bin\\FarCry2.exe here - this doesn't look like a Far Cry 2 folder.";
            return null;
        }
        if (!File.Exists(install.PatchFat) || !File.Exists(install.PatchDat))
        {
            error = "Data_Win32\\patch.fat / patch.dat are missing. Is the game fully installed and patched to 1.03?";
            return null;
        }

        return install;
    }

    /// <summary>Every .fat under Data_Win32, including DLC — none of them are special.</summary>
    public IEnumerable<string> EnumerateArchiveFats()
        => Directory.EnumerateFiles(DataDir, "*.fat", SearchOption.AllDirectories);

    /// <summary>
    /// Every .dat/.fat archive under Data_Win32 except patch.dat/.fat itself, as paths relative to
    /// <see cref="DataDir"/> — the base game's own files, which a legitimate install never modifies
    /// after the 1.03 patch. Checked against <see cref="VanillaHashes"/> once, at load, unlike
    /// patch.dat/.fat (see <see cref="PatchArchiveRelativePaths"/>), which the tool itself rewrites
    /// on every build and so is never a fair comparison here.
    /// </summary>
    public IEnumerable<string> EnumerateBaseArchiveRelativePaths()
    {
        foreach (string fat in EnumerateArchiveFats())
        {
            if (string.Equals(fat, PatchFat, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return Path.GetRelativePath(DataDir, fat);

            string dat = Path.ChangeExtension(fat, ".dat");
            if (File.Exists(dat))
            {
                yield return Path.GetRelativePath(DataDir, dat);
            }
        }
    }

    /// <summary><see cref="PatchFat"/>/<see cref="PatchDat"/>'s own paths, relative to
    /// <see cref="DataDir"/> — for validating a just-restored patch archive against
    /// <see cref="VanillaHashes"/> (see <c>MainWindow.RestoreVanilla_Click</c>).</summary>
    public IEnumerable<string> PatchArchiveRelativePaths()
    {
        yield return Path.GetRelativePath(DataDir, PatchFat);
        yield return Path.GetRelativePath(DataDir, PatchDat);
    }

    /// <summary>
    /// Creates the pristine backup if it doesn't exist yet.
    /// </summary>
    /// <remarks>
    /// The danger this guards against: a user who already has a modded patch.dat (from the old
    /// build_patch.bat workflow, or a downloaded mod) would otherwise have that mod frozen in as
    /// their "vanilla", permanently baked into every future build with no way to remove it. So we
    /// only ever create the backup from a patch we have reason to believe is untouched.
    /// </remarks>
    public void EnsureVanillaBackup(Func<bool>? confirmSuspiciousPatch = null)
    {
        if (HasVanillaBackup)
        {
            return;
        }

        if (LooksModded() && confirmSuspiciousPatch?.Invoke() == false)
        {
            throw new InvalidOperationException(
                "The current patch.dat looks like it already contains mods. Restore the original " +
                "patch.dat/patch.fat (verify the game files in Steam) before using this tool, or " +
                "it will treat someone else's mod as the base game.");
        }

        File.Copy(PatchFat, VanillaPatchFat, overwrite: false);
        File.Copy(PatchDat, VanillaPatchDat, overwrite: false);
    }

    /// <summary>
    /// A heuristic, not a proof: the stock 1.03 patch archive is ~3.5 KB of index and a few MB of
    /// data. A patch carrying a mod is normally much larger, and usually has far more entries.
    /// </summary>
    public bool LooksModded()
    {
        try
        {
            var index = Format.FatArchive.Read(PatchFat);
            const int stockEntryCount = 216;
            return index.Entries.Count > stockEntryCount + 8;
        }
        catch
        {
            return false;
        }
    }

    public void RestoreVanilla()
    {
        if (!HasVanillaBackup)
        {
            throw new InvalidOperationException("There is no vanilla backup to restore from.");
        }
        File.Copy(VanillaPatchFat, PatchFat, overwrite: true);
        File.Copy(VanillaPatchDat, PatchDat, overwrite: true);
    }
}
