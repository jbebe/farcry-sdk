using System.IO.Compression;
using System.Text;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Tests;

/// <summary>
/// A "legacy mod" here is built by running <see cref="PatchBuilder"/> itself against a throwaway copy
/// of the checked-in patch.dat/.fat fixture, then zipping up its output - that's exactly what the old
/// build_patch.bat-style workflow produces: a full replacement patch.dat/.fat, mostly vanilla bytes,
/// with the mod's actual edits mixed in. A second, untouched copy of the same fixture stands in for
/// "the base game" the import diffs against.
/// </summary>
public class LegacyPatchImporterTests : IDisposable
{
    private const string FixturesDir = "Fixtures/Patch";

    private readonly string _sandbox;
    private readonly GameInstall? _legacySourceInstall;
    private readonly GameInstall? _cleanInstall;

    public LegacyPatchImporterTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat))
        {
            return;
        }

        _legacySourceInstall = MakeFakeInstall("legacy_source", fixtureFat, fixtureDat);
        _cleanInstall = MakeFakeInstall("clean", fixtureFat, fixtureDat);
    }

    private GameInstall MakeFakeInstall(string name, string fixtureFat, string fixtureDat)
    {
        string root = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "Data_Win32"));
        File.WriteAllText(Path.Combine(root, "bin", "FarCry2.exe"), "stub");
        File.Copy(fixtureFat, Path.Combine(root, "Data_Win32", "patch.fat"));
        File.Copy(fixtureDat, Path.Combine(root, "Data_Win32", "patch.dat"));
        return GameInstall.TryOpen(root, out _)!;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            File.Exists(Path.Combine(FixturesDir, "patch.fat")) && File.Exists(Path.Combine(FixturesDir, "patch.dat")),
            $"{FixturesDir} had no patch.fat/patch.dat, so every test in this class silently no-opped.");

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Only_the_whole_file_and_fragment_changes_survive_the_import()
    {
        if (_legacySourceInstall is null || _cleanInstall is null) return;

        NameDatabase names = TestSupport.LoadNames();

        const string wholeFilePath = "engine/gamemodes/gamemodesconfig.xml";
        byte[] wholeFileContent = "legacy whole-file change"u8.ToArray();

        VfsFile container;
        string fragmentId;
        byte[] fragmentReplacementXml;
        using (var vfs = GameVfs.Load(_legacySourceInstall, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            fragmentId = fragment.FragmentId!;

            var replacement = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
            replacement.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
            fragmentReplacementXml = Encoding.UTF8.GetBytes(FcbXml.ToXml(replacement, FcbClassDefinitions.Empty).IndexXml);
        }

        var mod = MakeZipMod(
            "legacy_mod_source",
            (wholeFilePath, wholeFileContent),
            ($"{container.Path}\\{fragmentId}", fragmentReplacementXml));

        using (var vfsForRead = GameVfs.Load(_legacySourceInstall, names))
        {
            PatchBuilder.Build(_legacySourceInstall, [mod], vfsForRead.ReadOriginal);
        }

        // Zip up the just-built "legacy" patch.dat/.fat - stands in for the old-style mod a user would
        // have downloaded and copied straight into Data_Win32 by hand.
        string legacyZipPath = Path.Combine(_sandbox, "legacy_mod.zip");
        using (var zip = ZipFile.Open(legacyZipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(_legacySourceInstall.PatchFat, "Data_Win32/patch.fat");
            zip.CreateEntryFromFile(_legacySourceInstall.PatchDat, "Data_Win32/patch.dat");
        }

        string workspaceDir = Path.Combine(_sandbox, "workspace");
        Directory.CreateDirectory(workspaceDir);
        var workspace = new FolderModLayer(workspaceDir, "workspace");

        using var cleanVfs = GameVfs.Load(_cleanInstall, names);
        LegacyImportResult result = LegacyPatchImporter.Import(
            legacyZipPath, workspace, names, FcbClassDefinitions.Empty, cleanVfs.ReadOriginal);

        // Exactly the whole-file change and the one touched fragment get staged - every other archive
        // entry, plus every untouched sibling fragment of the container that was touched, is identical
        // (byte-for-byte, or logically for fragments) to the clean fixture and left out as noise.
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.FragmentsImported);
        Assert.True(result.Skipped > 0);

        workspace.Rescan();

        uint wholeFileHash = NameHash.Compute(wholeFilePath);
        Assert.Contains(wholeFileHash, workspace.Hashes);
        Assert.Equal(wholeFileContent, workspace.Read(wholeFileHash));

        // Not matched by id: FcbXml derives a fragment's id from its own content (the "Name" value),
        // and this hand-built replacement has none, so the id the splice landed at can legitimately
        // differ from the original fragmentId captured above (see PatchBuilderTests' identical note) -
        // what matters is that exactly one fragment of this container was staged, with the right bytes.
        Assert.True(workspace.FragmentOverrides.TryGetValue(container.Hash, out var overrides));
        FragmentOverride staged = Assert.Single(overrides!);
        Assert.Equal(fragmentReplacementXml, workspace.Read(staged.EntryHash));
    }

    [Fact]
    public void A_zip_with_no_patch_pair_is_rejected_rather_than_silently_no_opping()
    {
        Directory.CreateDirectory(_sandbox);
        string zipPath = Path.Combine(_sandbox, "not_legacy.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("worlds/world1/generated/foo.xml");
            using var stream = entry.Open();
            stream.Write("hi"u8);
        }

        string workspaceDir = Path.Combine(_sandbox, "workspace2");
        Directory.CreateDirectory(workspaceDir);
        var workspace = new FolderModLayer(workspaceDir, "workspace");

        Assert.Throws<InvalidDataException>(() => LegacyPatchImporter.Import(
            zipPath, workspace, NameDatabase.LoadFrom([]), FcbClassDefinitions.Empty, _ => null));
    }

    private ZipModLayer MakeZipMod(string name, params (string Path, byte[] Content)[] files)
    {
        string zipPath = Path.Combine(_sandbox, $"{name}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach ((string path, byte[] content) in files)
            {
                var entry = zip.CreateEntry(path);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }
        return new ZipModLayer(zipPath);
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
