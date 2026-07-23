using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Tests;

/// <summary>
/// The full pipeline against a real (copied) patch archive: mount it, resolve its names, stage an
/// override in a workspace folder, build, then re-open the result with the same reader the engine's
/// behaviour is modelled on.
///
/// The load-bearing assertion is the last one in
/// <see cref="A_build_overrides_the_target_and_leaves_every_other_file_readable"/>: after a build,
/// every file we did *not* touch must still decompress. That is what proves the builder's
/// copy-through path doesn't quietly corrupt the 213 LZO-compressed entries it shuffles around.
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly string _sandbox;
    private readonly GameInstall? _install;
    private readonly string _workspaceDir;

    public EndToEndTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-e2e", Guid.NewGuid().ToString("N"));
        _workspaceDir = Path.Combine(_sandbox, "workspace");
        Directory.CreateDirectory(_workspaceDir);

        string? real = FindRealInstall();
        if (real is null) return;

        Directory.CreateDirectory(Path.Combine(_sandbox, "game", "bin"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "game", "Data_Win32"));
        File.WriteAllText(Path.Combine(_sandbox, "game", "bin", "FarCry2.exe"), "stub");
        foreach (string ext in (string[])["fat", "dat"])
        {
            File.Copy(
                Path.Combine(real, "Data_Win32", $"patch.{ext}"),
                Path.Combine(_sandbox, "game", "Data_Win32", $"patch.{ext}"));
        }

        _install = GameInstall.TryOpen(Path.Combine(_sandbox, "game"), out _);
    }

    [Fact]
    public void The_merged_view_resolves_real_filenames_from_the_shipped_dictionary()
    {
        if (_install is null) return;

        using var vfs = GameVfs.Load(_install, TestSupport.LoadNames());

        Assert.NotEmpty(vfs.Files);

        // If the hash, the normalization, or the dictionary were wrong, nothing would resolve and
        // every file would land in _unknown.
        var named = vfs.Files.Values.Where(f => f.NameIsKnown).ToList();
        Assert.NotEmpty(named);
        Assert.Contains(named, f => f.Path.StartsWith("ui\\", StringComparison.Ordinal));

        // And the ones that don't resolve still get a usable identity rather than becoming opaque.
        foreach (VfsFile unnamed in vfs.Files.Values.Where(f => !f.NameIsKnown))
        {
            Assert.StartsWith("_unknown\\", unnamed.Path, StringComparison.Ordinal);
            Assert.NotEqual(string.Empty, unnamed.Type.Extension);
        }
    }

    [Fact]
    public void A_build_overrides_the_target_and_leaves_every_other_file_readable()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();

        // Pick a real, named, LZO-compressed file out of the real archive.
        VfsFile target;
        Dictionary<uint, byte[]> before;
        using (var vfs = GameVfs.Load(_install, names))
        {
            target = vfs.Files.Values.First(f => f.NameIsKnown && f.Size > 0);

            // Fragment rows are synthetic (one `.fcb` decoded into several virtual entries for the
            // tree/file view) and were never real archive entries, so they don't belong in a
            // "nothing else moved in the rebuilt archive" check.
            before = vfs.Files.Values.Where(f => !f.IsFragment).ToDictionary(f => f.Hash, f => vfs.Read(f.Hash));
        }

        byte[] replacement = "this is my modded file"u8.ToArray();

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(target.Hash, target.Path, target.Type.Extension, replacement);

        BuildResult result = PatchBuilder.Build(_install, [workspace]);
        Assert.Equal(1, result.OverriddenEntries);

        // Re-open the archive we just wrote, the same way the engine would read it.
        using var rebuilt = DuniaArchive.Open(_install.PatchFat);

        Assert.Equal(replacement, rebuilt.Read(target.Hash));

        // The real test: nothing else moved or broke. Every other entry must still decompress to
        // exactly the bytes it had before the build.
        int verified = 0;
        foreach ((uint hash, byte[] original) in before)
        {
            if (hash == target.Hash) continue;

            Assert.True(rebuilt.Contains(hash), $"Build dropped entry {hash:X8}.");
            Assert.Equal(original, rebuilt.Read(hash));
            verified++;
        }

        Assert.True(verified > 200, $"Only {verified} entries were checked; expected the whole patch archive.");
    }

    [Fact]
    public void Reverting_a_staged_edit_and_rebuilding_restores_the_original_bytes()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();

        VfsFile target;
        byte[] original;
        using (var vfs = GameVfs.Load(_install, names))
        {
            target = vfs.Files.Values.First(f => f.NameIsKnown && f.Size > 0);
            original = vfs.Read(target.Hash);
        }

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(target.Hash, target.Path, target.Type.Extension, "temporary"u8.ToArray());
        PatchBuilder.Build(_install, [workspace]);

        Assert.True(workspace.Unstage(target.Hash));
        PatchBuilder.Build(_install, [workspace]);

        using var rebuilt = DuniaArchive.Open(_install.PatchFat);

        // Round trip complete: the file is byte-for-byte what the game shipped, decompressed
        // through the same LZO path it originally came through.
        Assert.Equal(original, rebuilt.Read(target.Hash));
    }

    private static string? FindRealInstall()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2",
            @"D:\Steam\steamapps\common\Far Cry 2",
        ];
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "Data_Win32", "patch.fat")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        }
        catch { /* temp cleanup is best-effort */ }
    }
}
