using System.Security.Cryptography;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Tools.Fixtures;

namespace JackAll.Tests;

/// <summary>
/// The fixtures' manifest: a restore brings back what it lists from an install's archives and the
/// Nexus downloads, and every fixture file in the checkout is listed with its bytes.
/// </summary>
public sealed class FixtureManifestTests : IDisposable
{
    private const string EntryPath = @"engine\gamemodes\gamemodesconfig.xml";
    private const string Page = "https://www.nexusmods.com/farcry2/mods/279";
    private static readonly byte[] Content = "<GameModes/>"u8.ToArray();

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Every_fixture_file_is_in_the_manifest_with_its_bytes()
    {
        string folder = Path.Combine(TestSupport.RepositoryRoot, "tools", "JackAll", "src", "JackAll.Tests", "Fixtures");

        Assert.Empty(FixtureManifest.Unrecorded(folder, FixtureManifest.Read(folder)));
    }

    [Fact]
    public void A_missing_fixture_is_restored_from_its_archive_entry_and_left_alone_after()
    {
        GameInstall install = MakeInstall();
        string fixtures = MakeFixtures(install, new FixtureLine("Ai/gamemodesconfig.xml", Sha(Content), "patch.fat", EntryPath));

        FixtureReport first = FixtureManifest.Restore(fixtures, install, _sandbox);
        FixtureReport second = FixtureManifest.Restore(fixtures, install, _sandbox);

        Assert.Equal((2, 0), (first.Written, first.Unchanged));
        Assert.Empty(first.Problems);
        Assert.Equal(Content, File.ReadAllBytes(Path.Combine(fixtures, "Ai", "gamemodesconfig.xml")));
        Assert.Equal((0, 2), (second.Written, second.Unchanged));
    }

    [Fact]
    public void An_install_with_another_patch_is_refused()
    {
        GameInstall install = MakeInstall();
        string fixtures = MakeFixtures(install);
        File.WriteAllBytes(install.PatchFat, [0]);

        Assert.Throws<InvalidOperationException>(() => FixtureManifest.Restore(fixtures, install, _sandbox));
    }

    [Fact]
    public void Record_adds_the_line_of_a_new_file_and_keeps_the_lines_of_files_not_present()
    {
        GameInstall install = MakeInstall();
        string fixtures = MakeFixtures(install,
            new FixtureLine("Mods/relaxed/patch.dat", Sha([1]), Page, "Far Cry 2 Relaxed-279-1-2.zip/patch.dat"));
        Directory.CreateDirectory(Path.Combine(fixtures, "Ai"));
        File.WriteAllBytes(Path.Combine(fixtures, "Ai", "gamemodesconfig.xml"), Content);

        FixtureReport report = FixtureManifest.Record(fixtures, install);

        Assert.Equal(1, report.Written);
        Assert.Empty(report.Problems);
        Assert.Equal(["Ai/gamemodesconfig.xml", "Mods/relaxed/patch.dat", "Patch/patch.fat"],
            FixtureManifest.Read(fixtures).Select(line => line.Fixture));
    }

    [Fact]
    public void A_download_missing_from_the_mods_folder_fails_while_the_rest_is_restored()
    {
        GameInstall install = MakeInstall();
        string fixtures = MakeFixtures(install,
            new FixtureLine("Ai/gamemodesconfig.xml", Sha(Content), "patch.fat", EntryPath),
            new FixtureLine("Mods/relaxed/patch.dat", Sha([1]), Page, "Far Cry 2 Relaxed-279-1-2.zip/patch.dat"));

        FixtureReport report = FixtureManifest.Restore(fixtures, install, _sandbox);

        Assert.Equal(2, report.Written);
        Assert.Contains(Page, Assert.Single(report.Problems));
    }

    /// <summary>A Far Cry 2 folder whose patch archive holds one stored entry, <see cref="Content"/>.</summary>
    private GameInstall MakeInstall()
    {
        string root = Path.Combine(_sandbox, "game");
        string data = Path.Combine(root, "Data_Win32");
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(root, "bin", "FarCry2.exe"), "stub");
        TestSupport.WriteSingleEntryArchive(Path.Combine(data, "patch"), NameHash.Compute(EntryPath), Content);
        return GameInstall.TryOpen(root, out string error) ?? throw new InvalidOperationException(error);
    }

    /// <summary>A fixtures folder whose manifest lists <paramref name="lines"/> after the install's own patch index.</summary>
    private string MakeFixtures(GameInstall install, params FixtureLine[] lines)
    {
        string fixtures = Path.Combine(_sandbox, "fixtures");
        Directory.CreateDirectory(fixtures);
        FixtureManifest.Write(fixtures,
            [new FixtureLine("Patch/patch.fat", Sha(File.ReadAllBytes(install.PatchFat)), "patch.fat", ""), .. lines]);
        return fixtures;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }
}
