using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Tests;

/// <summary>
/// The cache exists purely to avoid ~50,000 random header reads and ~46,000 `.fcb` decodes on every
/// launch. So the tests that matter are: does a warm cache produce <em>exactly</em> the same answers
/// as a cold one (a cache that's fast but wrong is worse than no cache), and does it actually avoid
/// the work.
/// </summary>
public class GameCacheTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string _cachePath;

    public GameCacheTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "jackall-cache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        _cachePath = Path.Combine(_cacheDir, ".cache");
    }

    private const string FixturesDir = "Fixtures/Patch";

    /// <summary>
    /// Builds a throwaway install from just the checked-in patch.dat/.fat fixture, instead of a real
    /// (tens-of-thousands-of-files) game folder. GameVfs treats whatever sits at install.PatchFat as
    /// the volatile, never-cached archive, so the fixture is mounted under a different name — mounting
    /// it as "patch.fat" would make every entry uncacheable and this suite couldn't prove anything.
    /// patch.fat/.dat still need to exist to satisfy GameInstall.TryOpen; they're stubs GameVfs skips
    /// because they fail to parse as a real archive.
    /// </summary>
    private static GameInstall? OpenFixtureInstall(string sandbox)
    {
        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat))
        {
            return null;
        }

        Directory.CreateDirectory(Path.Combine(sandbox, "bin"));
        Directory.CreateDirectory(Path.Combine(sandbox, "Data_Win32"));
        File.WriteAllText(Path.Combine(sandbox, "bin", "FarCry2.exe"), "stub");
        File.WriteAllText(Path.Combine(sandbox, "Data_Win32", "patch.fat"), "not a real archive");
        File.WriteAllText(Path.Combine(sandbox, "Data_Win32", "patch.dat"), "not a real archive");
        File.Copy(fixtureFat, Path.Combine(sandbox, "Data_Win32", "common.fat"));
        File.Copy(fixtureDat, Path.Combine(sandbox, "Data_Win32", "common.dat"));

        return GameInstall.TryOpen(sandbox, out _);
    }

    // ------------------------------------------------------------------ type section

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_warm_cache_yields_the_identical_merged_view_and_skips_the_header_reads()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "jackall-cache-install", Guid.NewGuid().ToString("N"));
        var install = OpenFixtureInstall(sandbox);
        if (install is null) return;

        try
        {
            NameDatabase names = TestSupport.LoadNames();

            // Cold: no cache file. Every nameless entry is sniffed from the archive.
            var cold = GameCache.Load(_cachePath);
            Assert.Equal(0, cold.TypeCount);

            Dictionary<uint, string> coldTypes;
            using (var vfs = GameVfs.Load(install, names, cold))
            {
                coldTypes = vfs.Files.Values.ToDictionary(f => f.Hash, f => $"{f.Type.Category}/{f.Type.Extension}");
            }

            // The fixture is a small subset of a real install (216 entries, 4 nameless), so this
            // just proves the cold pass sniffed every nameless one — not the "tens of thousands" a
            // real install would produce.
            Assert.True(cold.IsDirty, "A cold load must have sniffed something worth writing down.");
            Assert.True(cold.TypeCount > 0, "No types were cached; expected the fixture's nameless entries to be sniffed.");
            cold.Save(_cachePath);
            Assert.False(cold.IsDirty);

            // Warm: reload from the file.
            var warm = GameCache.Load(_cachePath);
            Assert.Equal(cold.TypeCount, warm.TypeCount);

            Dictionary<uint, string> warmTypes;
            using (var vfs = GameVfs.Load(install, names, warm))
            {
                warmTypes = vfs.Files.Values.ToDictionary(f => f.Hash, f => $"{f.Type.Category}/{f.Type.Extension}");
            }

            // The whole point. Same answers, every file, or the cache is a liar.
            Assert.Equal(coldTypes, warmTypes);

            // And no entry was sniffed again — a miss would have dirtied the cache. (patch.dat is never
            // cached, but it's sniffed outside the cache entirely, so it can't dirty it either.)
            Assert.False(warm.IsDirty, "A warm load re-sniffed something, so the cache isn't covering what it should.");
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Saving_over_an_existing_cache_leaves_no_temp_file_behind()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "jackall-cache-install", Guid.NewGuid().ToString("N"));
        var install = OpenFixtureInstall(sandbox);
        if (install is null) return;

        try
        {
            NameDatabase names = TestSupport.LoadNames();

            var cache = GameCache.Load(_cachePath);
            using (GameVfs.Load(install, names, cache)) { }

            cache.Save(_cachePath);
            cache.Save(_cachePath);

            Assert.True(File.Exists(_cachePath));
            Assert.False(File.Exists(_cachePath + ".writing"), "The temp file survived the swap.");
            Assert.Equal(cache.TypeCount, GameCache.Load(_cachePath).TypeCount);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    // ------------------------------------------------------------------ fcb structure section

    [Fact]
    public void Set_then_TryGet_round_trips_including_an_empty_fragment_list()
    {
        var cache = new GameCache();

        cache.Set(0x11111111, [new FcbFragmentInfo("01_A.xml", 120), new FcbFragmentInfo("02_B.xml", 340)]);
        cache.Set(0x22222222, []); // "doesn't split" is a real, cacheable answer too

        Assert.True(cache.TryGet(0x11111111, out var withFragments));
        Assert.Equal([new FcbFragmentInfo("01_A.xml", 120), new FcbFragmentInfo("02_B.xml", 340)], withFragments);

        Assert.True(cache.TryGet(0x22222222, out var noFragments));
        Assert.Empty(noFragments);

        Assert.False(cache.TryGet(0x33333333, out _));
        Assert.True(cache.IsDirty);
    }

    [Fact]
    public void Save_then_Load_reproduces_the_exact_same_fragment_answers()
    {
        var cache = new GameCache();
        cache.Set(0xDEADBEEF,
        [
            new FcbFragmentInfo("03_Outpost_West.xml", 4096),
            new FcbFragmentInfo("04_Outpost_East.xml", 8192),
        ]);
        cache.Set(0xCAFEF00D, []);
        cache.Save(_cachePath);

        var reloaded = GameCache.Load(_cachePath);

        Assert.Equal(cache.FragmentContainerCount, reloaded.FragmentContainerCount);
        Assert.True(reloaded.TryGet(0xDEADBEEF, out var fragments));
        Assert.Equal(
        [
            new FcbFragmentInfo("03_Outpost_West.xml", 4096),
            new FcbFragmentInfo("04_Outpost_East.xml", 8192),
        ], fragments);
        Assert.True(reloaded.TryGet(0xCAFEF00D, out var empty));
        Assert.Empty(empty);
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_warm_cache_yields_identical_fragment_rows_to_a_cold_decode()
    {
        // A real install has tens of thousands of files across every archive, far more than this
        // test needs to prove the cache round-trips correctly, so it mounts only the checked-in
        // patch.dat/.fat fixture instead.
        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat)) return;

        string sandbox = Path.Combine(Path.GetTempPath(), "jackall-fcbcache-install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(sandbox, "bin"));
        Directory.CreateDirectory(Path.Combine(sandbox, "Data_Win32"));
        File.WriteAllText(Path.Combine(sandbox, "bin", "FarCry2.exe"), "stub");

        // GameVfs treats whatever sits at install.PatchFat as the volatile, never-cached archive
        // (it's the one the builder rewrites on every run), so the fixture can't be mounted under
        // that name or none of its .fcb entries would ever make it into the cache. patch.fat/.dat
        // just need to exist to satisfy GameInstall.TryOpen; GameVfs silently skips them when they
        // fail to parse.
        File.WriteAllText(Path.Combine(sandbox, "Data_Win32", "patch.fat"), "not a real archive");
        File.WriteAllText(Path.Combine(sandbox, "Data_Win32", "patch.dat"), "not a real archive");
        File.Copy(fixtureFat, Path.Combine(sandbox, "Data_Win32", "common.fat"));
        File.Copy(fixtureDat, Path.Combine(sandbox, "Data_Win32", "common.dat"));

        try
        {
            var install = GameInstall.TryOpen(sandbox, out _)!;
            NameDatabase names = TestSupport.LoadNames();

            var cold = GameCache.Load(_cachePath);
            Dictionary<uint, VfsFile> coldFragments;
            using (var vfs = GameVfs.Load(install, names, cold))
            {
                coldFragments = vfs.Files.Values.Where(f => f.IsFragment).ToDictionary(f => f.Hash);
            }
            Assert.True(cold.IsDirty, "A cold load must have decoded at least one .fcb worth writing down.");
            cold.Save(_cachePath);

            var warm = GameCache.Load(_cachePath);
            Dictionary<uint, VfsFile> warmFragments;
            Dictionary<uint, VfsFile> allWarmFiles;
            using (var vfs = GameVfs.Load(install, names, warm))
            {
                allWarmFiles = vfs.Files.ToDictionary(kv => kv.Key, kv => kv.Value);
                warmFragments = allWarmFiles.Values.Where(f => f.IsFragment).ToDictionary(f => f.Hash);
            }

            Assert.Equal(coldFragments.Keys.OrderBy(h => h), warmFragments.Keys.OrderBy(h => h));
            Assert.False(warm.IsDirty, "A warm load re-decoded something the cache should have covered.");

            // The claim the whole feature rests on: a fragment's directory is exactly its container's
            // own path, so the tree view gets a folder node for free with no dedicated widget code.
            Assert.NotEmpty(warmFragments);
            foreach (VfsFile fragment in warmFragments.Values)
            {
                VfsFile container = allWarmFiles[fragment.ContainerHash!.Value];
                Assert.Equal(container.Path, fragment.Directory);
            }

            // Regression coverage: fragment rows must carry a real rendered size, not the placeholder
            // 0 an earlier version of this feature shipped with.
            Assert.All(warmFragments.Values, f => Assert.True(f.Size > 0, $"'{f.Path}' has no size."));
            Assert.Equal(
                coldFragments.Values.Select(f => f.Size).OrderBy(s => s),
                warmFragments.Values.Select(f => f.Size).OrderBy(s => s));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    // ------------------------------------------------------------------ shared persistence behavior

    [Fact]
    public void A_corrupt_cache_file_is_ignored_rather_than_crashing_the_app()
    {
        // Truncated, garbage, half-written by a crash — all of it must degrade to "no cache", never
        // to an exception, because none of this file's contents are load-bearing.
        File.WriteAllBytes(_cachePath, [0x4A, 0x41, 0x43, 0x31, 0xFF, 0xFF]);
        Assert.Equal(0, GameCache.Load(_cachePath).TypeCount);

        File.WriteAllText(_cachePath, "this is not a cache file, it is a poem about one");
        Assert.Equal(0, GameCache.Load(_cachePath).TypeCount);

        File.WriteAllBytes(_cachePath, []);
        Assert.Equal(0, GameCache.Load(_cachePath).TypeCount);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
        }
        catch { /* temp cleanup is best-effort */ }
    }
}
