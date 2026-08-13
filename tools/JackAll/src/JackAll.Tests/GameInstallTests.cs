using JackAll.Core;
using JackAll.Core.Format;

namespace JackAll.Tests;

/// <summary>
/// The backup rules, which are what stand between a user and an unrecoverable install.
/// </summary>
public class GameInstallTests : IDisposable
{
    private const string FixturesDir = "Fixtures/Patch";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_folder_without_the_exe_is_rejected_with_a_reason()
    {
        string root = Path.Combine(_sandbox, "empty");
        Directory.CreateDirectory(root);

        Assert.Null(GameInstall.TryOpen(root, out string error));
        Assert.Contains("FarCry2.exe", error);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void EnsureVanillaBackup_without_a_confirmation_delegate_does_not_refuse_a_modded_patch()
    {
        GameInstall? install = MakeInstall("suspicious");
        if (install is null) return;

        // Make the patch look like it already carries a mod, which is the state that must never be
        // frozen in as "vanilla".
        InflateEntryCount(install);
        Assert.True(install.LooksModded());
        Assert.False(install.HasVanillaBackup);

        // This is the sharp edge, pinned deliberately rather than fixed here: the guard inside
        // EnsureVanillaBackup is `LooksModded() && confirmSuspiciousPatch?.Invoke() == false`, and
        // with a null delegate that second half is false, so the modded patch is backed up anyway.
        // JackAll.App never reaches it (it always passes a callback); anything headless has nobody to
        // ask and therefore has to do its own check first - which is what jackall-cli's
        // `mod build` guard is, and why it can't be deleted as redundant.
        install.EnsureVanillaBackup();
        Assert.True(install.HasVanillaBackup);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void EnsureVanillaBackup_refuses_a_modded_patch_when_the_caller_declines_to_confirm()
    {
        GameInstall? install = MakeInstall("declined");
        if (install is null) return;

        InflateEntryCount(install);

        Assert.Throws<InvalidOperationException>(() => install.EnsureVanillaBackup(confirmSuspiciousPatch: () => false));
        Assert.False(install.HasVanillaBackup);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Restore_puts_the_backed_up_bytes_back()
    {
        GameInstall? install = MakeInstall("restore");
        if (install is null) return;

        byte[] original = File.ReadAllBytes(install.PatchDat);
        install.EnsureVanillaBackup();
        File.WriteAllBytes(install.PatchDat, "clobbered"u8.ToArray());

        install.RestoreVanilla();

        Assert.Equal(original, File.ReadAllBytes(install.PatchDat));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void BackupWouldCaptureMods_flags_exactly_the_modded_no_backup_state()
    {
        GameInstall? install = MakeInstall("capture-check");
        if (install is null) return;

        Assert.False(install.BackupWouldCaptureMods);

        InflateEntryCount(install);
        Assert.True(install.BackupWouldCaptureMods);

        install.EnsureVanillaBackup();
        Assert.False(install.BackupWouldCaptureMods);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void TryCountPatchEntries_reads_the_index_and_reports_unreadable_as_minus_one()
    {
        GameInstall? install = MakeInstall("count");
        if (install is null) return;

        Assert.Equal(FatArchive.Read(install.PatchFat).Entries.Count, install.TryCountPatchEntries());

        File.WriteAllBytes(install.PatchFat, "garbage"u8.ToArray());
        Assert.Equal(-1, install.TryCountPatchEntries());
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void EnumerateArchiveFats_excludes_the_vanilla_backup_pair()
    {
        GameInstall? install = MakeInstall("enumerate");
        if (install is null) return;

        install.EnsureVanillaBackup();

        string[] fats = [.. install.EnumerateArchiveFats()];
        Assert.Contains(install.PatchFat, fats);
        Assert.DoesNotContain(install.VanillaPatchFat, fats);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void ReadBaseGameHashes_reads_the_backup_not_the_live_patch_once_one_exists()
    {
        GameInstall? install = MakeInstall("base-hashes");
        if (install is null) return;

        install.EnsureVanillaBackup();
        HashSet<uint> vanilla = install.ReadBaseGameHashes();

        // Entries added by a later build are JackAll's own output, not base-game files.
        InflateEntryCount(install);
        HashSet<uint> afterBuild = install.ReadBaseGameHashes();

        Assert.Equal(vanilla, afterBuild);
    }

    /// <summary>Rewrites the index with enough duplicate entries to trip
    /// <see cref="GameInstall.LooksModded"/>'s entry-count heuristic, without touching the .dat -
    /// the heuristic only ever reads the index.</summary>
    private static void InflateEntryCount(GameInstall install)
    {
        FatArchive index = FatArchive.Read(install.PatchFat);
        List<FatEntry> entries = [.. index.Entries];
        uint nextHash = entries.Max(e => e.Hash) + 1;
        for (int i = 0; i < 32; i++)
        {
            entries.Add(entries[0] with { Hash = nextHash++ });
        }
        FatArchive.FromEntries(entries, index.Flags).Write(install.PatchFat);
    }

    private GameInstall? MakeInstall(string name)
    {
        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat))
        {
            return null;
        }

        string root = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "Data_Win32"));
        File.WriteAllText(Path.Combine(root, "bin", "FarCry2.exe"), "stub");
        File.Copy(fixtureFat, Path.Combine(root, "Data_Win32", "patch.fat"));
        File.Copy(fixtureDat, Path.Combine(root, "Data_Win32", "patch.dat"));
        return GameInstall.TryOpen(root, out _);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        }
        catch { /* temp dir cleanup is best-effort */ }
    }
}
