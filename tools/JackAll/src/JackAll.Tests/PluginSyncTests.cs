using System.IO.Compression;
using System.Text;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// The reserved <c>plugins\</c> and <c>mods\</c> layer folders: layers surface the plugin payload
/// instead of hashing it into patch.dat, and <see cref="PluginSync"/> mirrors it into
/// <c>bin\plugins</c> with the manifest deciding which files are JackAll's to overwrite or remove.
/// None of this parses the archives, so a stub install with no fixture is enough.
/// </summary>
public class PluginSyncTests : IDisposable
{
    private readonly string _sandbox;
    private readonly GameInstall _install;

    public PluginSyncTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "bin"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "Data_Win32"));
        File.WriteAllText(Path.Combine(_sandbox, "bin", "FarCry2.exe"), "stub");
        File.WriteAllText(Path.Combine(_sandbox, "Data_Win32", "patch.fat"), "fat");
        File.WriteAllText(Path.Combine(_sandbox, "Data_Win32", "patch.dat"), "dat");
        _install = GameInstall.TryOpen(_sandbox, out _)!;
    }

    [Fact]
    public void A_folder_layer_splits_plugin_payload_from_hashed_content()
    {
        FolderModLayer layer = MakeFolderLayer("m",
            (@"plugins\a.dll", "dll"),
            (@"plugins\sub\b.lua", "lua"),
            (@"plugins\__folder_managed_by_vortex", ""),
            (@"generated\x.xml", "xml"));

        Assert.Equal([NameHash.Compute(@"generated\x.xml")], layer.Hashes);
        Assert.Equal(["a.dll", @"sub\b.lua"], layer.PluginPaths.Order());
        Assert.Equal("lua", Encoding.UTF8.GetString(layer.ReadPlugin(@"sub\b.lua")));
    }

    [Fact]
    public void A_zip_layer_surfaces_the_same_payload()
    {
        ZipModLayer layer = MakeZipLayer("z",
            ("plugins/a.dll", "dll"),
            ("mods/engine/gamemodes/gamemodesconfig.xml", "xml"));

        Assert.Equal(["a.dll"], layer.PluginPaths);
        Assert.Equal([NameHash.Compute(@"engine\gamemodes\gamemodesconfig.xml")], layer.Hashes);
        Assert.Equal("dll", Encoding.UTF8.GetString(layer.ReadPlugin("a.dll")));
    }

    [Fact]
    public void A_mods_wrapper_hashes_to_the_same_entry_as_root_layout()
    {
        const string gamePath = @"engine\gamemodes\gamemodesconfig.xml";
        FolderModLayer layer = MakeFolderLayer("w", ($@"mods\{gamePath}", "x"));

        Assert.Equal([NameHash.Compute(gamePath)], layer.Hashes);
        Assert.Equal(gamePath, layer.PathOf(NameHash.Compute(gamePath)));
    }

    [Fact]
    public void Stage_writes_under_the_mods_wrapper_and_round_trips()
    {
        const string gamePath = @"worlds\world1\a.xml";
        FolderModLayer layer = MakeFolderLayer("stage");

        layer.Stage(NameHash.Compute(gamePath), gamePath, "xml", "x"u8.ToArray());

        Assert.True(File.Exists(Path.Combine(layer.RootPath, "mods", "worlds", "world1", "a.xml")));
        Assert.Equal(gamePath, layer.PathOf(NameHash.Compute(gamePath)));
        // A fresh scan of the same folder classifies identically to the in-place update.
        Assert.Equal([NameHash.Compute(gamePath)], new FolderModLayer(layer.RootPath, "rescan").Hashes);
    }

    [Fact]
    public void Staging_a_game_path_that_starts_with_plugins_stays_content()
    {
        const string gamePath = @"plugins\engine\foo.xml";
        FolderModLayer layer = MakeFolderLayer("collision");

        layer.Stage(NameHash.Compute(gamePath), gamePath, "xml", "x"u8.ToArray());

        // The mods\ wrapper keeps a reserved-looking game path out of the plugin payload.
        Assert.Empty(layer.PluginPaths);
        Assert.Equal([NameHash.Compute(gamePath)], layer.Hashes);
        Assert.Empty(new FolderModLayer(layer.RootPath, "rescan").PluginPaths);
    }

    [Fact]
    public void Restaging_a_root_layout_override_moves_it_under_mods()
    {
        const string gamePath = @"worlds\a.xml";
        FolderModLayer layer = MakeFolderLayer("migrate", (gamePath, "old"));

        layer.Stage(NameHash.Compute(gamePath), gamePath, "xml", "new"u8.ToArray());

        Assert.False(Directory.Exists(Path.Combine(layer.RootPath, "worlds")));
        Assert.Equal("new", Encoding.UTF8.GetString(layer.Read(NameHash.Compute(gamePath))));
        Assert.True(File.Exists(Path.Combine(layer.RootPath, "mods", "worlds", "a.xml")));
    }

    [Fact]
    public void Apply_deploys_the_payload_and_writes_the_manifest()
    {
        var layer = MakeFolderLayer("m", (@"plugins\a.dll", "dll"), (@"plugins\sub\b.lua", "lua"));

        PluginSyncResult result = PluginSync.Apply(_install, [layer]);

        Assert.Equal(2, result.Deployed);
        Assert.Equal("dll", File.ReadAllText(PluginFile("a.dll")));
        Assert.Equal("lua", File.ReadAllText(PluginFile(@"sub\b.lua")));
        Assert.True(File.Exists(PluginFile(PluginSync.ManifestFileName)));
    }

    [Fact]
    public void The_later_layer_wins_a_shared_plugin_path()
    {
        var first = MakeFolderLayer("first", (@"plugins\same.dll", "old"));
        var second = MakeFolderLayer("second", (@"plugins\same.dll", "new"));

        PluginSync.Apply(_install, [first, second]);

        Assert.Equal("new", File.ReadAllText(PluginFile("same.dll")));
    }

    [Fact]
    public void Disabling_the_source_layer_removes_its_files_and_prunes_empty_folders()
    {
        var layer = MakeFolderLayer("m", (@"plugins\sub\b.lua", "x"));
        PluginSync.Apply(_install, [layer]);

        layer.Enabled = false;
        PluginSyncResult result = PluginSync.Apply(_install, [layer]);

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(PluginFile(@"sub\b.lua")));
        Assert.False(Directory.Exists(Path.Combine(PluginsDir, "sub")));
        // bin\plugins itself stays - FCSE expects it - but an empty manifest is deleted.
        Assert.True(Directory.Exists(PluginsDir));
        Assert.False(File.Exists(PluginFile(PluginSync.ManifestFileName)));
    }

    [Fact]
    public void A_build_with_no_layers_clears_previously_tracked_files()
    {
        PluginSync.Apply(_install, [MakeFolderLayer("m", (@"plugins\a.dll", "x"))]);

        PluginSyncResult result = PluginSync.Apply(_install, []);

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(PluginFile("a.dll")));
    }

    [Fact]
    public void A_foreign_file_is_never_overwritten_or_removed()
    {
        Directory.CreateDirectory(PluginsDir);
        File.WriteAllText(PluginFile("same.dll"), "user's own");

        var layer = MakeFolderLayer("m", (@"plugins\same.dll", "mod's"));
        PluginSyncResult applied = PluginSync.Apply(_install, [layer]);

        Assert.Equal(["same.dll"], applied.SkippedForeign);
        Assert.Equal("user's own", File.ReadAllText(PluginFile("same.dll")));

        PluginSyncResult removed = PluginSync.RemoveAll(_install);
        Assert.Equal(0, removed.Removed);
        Assert.Equal("user's own", File.ReadAllText(PluginFile("same.dll")));
    }

    [Fact]
    public void An_identical_untracked_file_is_adopted_rather_than_flagged()
    {
        Directory.CreateDirectory(PluginsDir);
        File.WriteAllText(PluginFile("same.dll"), "same bytes");

        var layer = MakeFolderLayer("m", (@"plugins\same.dll", "same bytes"));
        PluginSyncResult applied = PluginSync.Apply(_install, [layer]);

        Assert.Empty(applied.SkippedForeign);
        Assert.Equal(1, applied.Deployed);

        layer.Enabled = false;
        Assert.Equal(1, PluginSync.Apply(_install, [layer]).Removed);
        Assert.False(File.Exists(PluginFile("same.dll")));
    }

    [Fact]
    public void RemoveAll_without_a_manifest_touches_nothing()
    {
        PluginSyncResult result = PluginSync.RemoveAll(_install);

        Assert.Equal(0, result.Removed);
        Assert.False(Directory.Exists(PluginsDir));
    }

    [Fact]
    public void RestoreVanilla_also_removes_the_deployed_plugins()
    {
        File.Copy(_install.PatchFat, _install.VanillaPatchFat);
        File.Copy(_install.PatchDat, _install.VanillaPatchDat);
        PluginSync.Apply(_install, [MakeFolderLayer("m", (@"plugins\a.dll", "x"))]);

        PluginSyncResult result = _install.RestoreVanilla();

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(PluginFile("a.dll")));
    }

    [Fact]
    public void An_unreadable_manifest_is_treated_as_empty()
    {
        Directory.CreateDirectory(PluginsDir);
        File.WriteAllText(PluginFile(PluginSync.ManifestFileName), "{not json");

        PluginSyncResult result = PluginSync.Apply(_install, [MakeFolderLayer("m", (@"plugins\a.dll", "x"))]);

        Assert.Equal(1, result.Deployed);
        Assert.Equal("x", File.ReadAllText(PluginFile("a.dll")));
    }

    private string PluginsDir => PluginSync.PluginsDir(_install);

    private string PluginFile(string relative) => Path.Combine(PluginsDir, relative);

    private FolderModLayer MakeFolderLayer(string name, params (string Path, string Content)[] files)
    {
        string root = Path.Combine(_sandbox, "layers", name);
        Directory.CreateDirectory(root);
        foreach ((string path, string content) in files)
        {
            string absolute = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, content);
        }
        return new FolderModLayer(root, name);
    }

    private ZipModLayer MakeZipLayer(string name, params (string Path, string Content)[] files)
    {
        string zipPath = Path.Combine(_sandbox, $"{name}.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach ((string path, string content) in files)
            {
                var entry = zip.CreateEntry(path);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
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
