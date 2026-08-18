using System.IO.Compression;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The builder writes to the user's game folder, so these tests run against a throwaway copy built
/// from a checked-in patch.dat/.fat fixture (extracted from a real install) rather than the game
/// folder itself. What's being pinned down here is the property the whole safety story rests on:
/// the output depends only on the vanilla backup and the enabled layers, never on what is currently
/// sitting in patch.dat.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class PatchBuilderTests : IDisposable
{
    private const string FixturesDir = "Fixtures/Patch";

    private readonly string _sandbox;
    private readonly GameInstall? _install;

    public PatchBuilderTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat))
        {
            return;
        }

        // A fake install: real patch.dat/.fat (the thing under test), stub exe, nothing else. This
        // keeps the copy at a few MB instead of the ~5 GB the whole game would cost.
        Directory.CreateDirectory(Path.Combine(_sandbox, "bin"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "Data_Win32"));
        File.WriteAllText(Path.Combine(_sandbox, "bin", "FarCry2.exe"), "stub");
        File.Copy(fixtureFat, Path.Combine(_sandbox, "Data_Win32", "patch.fat"));
        File.Copy(fixtureDat, Path.Combine(_sandbox, "Data_Win32", "patch.dat"));

        _install = GameInstall.TryOpen(_sandbox, out _);
    }

    [Fact]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            File.Exists(Path.Combine(FixturesDir, "patch.fat")) && File.Exists(Path.Combine(FixturesDir, "patch.dat")),
            $"{FixturesDir} had no patch.fat/patch.dat, so every test in this class silently no-opped.");

    [Fact]
    public void Building_with_no_mods_reproduces_the_vanilla_patch_exactly()
    {
        if (_install is null) return;

        byte[] originalDat = File.ReadAllBytes(_install.PatchDat);
        byte[] originalFat = File.ReadAllBytes(_install.PatchFat);

        PatchBuilder.Build(_install, []);

        // Not merely "the game still runs": the bytes are identical. If this holds, the builder is
        // faithfully round-tripping every vanilla entry, including all 213 LZO-compressed ones.
        Assert.Equal(originalDat, File.ReadAllBytes(_install.PatchDat));
        Assert.Equal(originalFat, File.ReadAllBytes(_install.PatchFat));
    }

    [Fact]
    public void Building_repeatedly_is_idempotent()
    {
        if (_install is null) return;

        var mod = MakeZipMod("test_mod", ("mods/engine/gamemodes/gamemodesconfig.xml", "hello"u8.ToArray()));

        PatchBuilder.Build(_install, [mod]);
        byte[] first = File.ReadAllBytes(_install.PatchDat);

        PatchBuilder.Build(_install, [mod]);
        byte[] second = File.ReadAllBytes(_install.PatchDat);

        // The trap this guards: a builder that reads the live patch.dat would append the mod again
        // on every build, growing the archive without bound.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Disabling_a_mod_and_rebuilding_restores_the_vanilla_bytes()
    {
        if (_install is null) return;

        byte[] vanilla = File.ReadAllBytes(_install.PatchDat);
        var mod = MakeZipMod("test_mod", ("mods/engine/gamemodes/gamemodesconfig.xml", "hello"u8.ToArray()));

        PatchBuilder.Build(_install, [mod]);
        Assert.NotEqual(vanilla, File.ReadAllBytes(_install.PatchDat));

        mod.Enabled = false;
        PatchBuilder.Build(_install, [mod]);

        // "Uninstalling" a mod has to actually uninstall it, with no reinstall required.
        Assert.Equal(vanilla, File.ReadAllBytes(_install.PatchDat));
    }

    [Fact]
    public void An_override_replaces_the_vanilla_entry_rather_than_duplicating_its_hash()
    {
        if (_install is null) return;

        const string path = "engine/gamemodes/gamemodesconfig.xml";
        uint hash = NameHash.Compute(path);
        byte[] content = "modded content"u8.ToArray();

        int vanillaCount = FatArchive.Read(_install.PatchFat).Entries.Count;
        var result = PatchBuilder.Build(_install, [MakeZipMod("m", ($"mods/{path}", content))]);

        var index = FatArchive.Read(_install.PatchFat);

        // One hash, one entry — a duplicate would make the engine's binary search pick arbitrarily.
        Assert.Single(index.Entries, e => e.Hash == hash);
        Assert.Equal(vanillaCount, index.Entries.Count);
        Assert.Equal(1, result.OverriddenEntries);
        Assert.Equal(0, result.AddedEntries);

        var entry = index.Entries.First(e => e.Hash == hash);
        Assert.Equal(CompressionScheme.None, entry.Compression);

        using var dat = File.OpenRead(_install.PatchDat);
        var actual = new byte[entry.StoredSize];
        dat.Seek(entry.Offset, SeekOrigin.Begin);
        dat.ReadExactly(actual);
        Assert.Equal(content, actual);
    }

    [Fact]
    public void A_file_with_an_unknown_name_can_still_be_overridden_via_the_hash_folder()
    {
        if (_install is null) return;

        // The whole point of the _hash convention: entries whose filename nobody has recovered are
        // still moddable, because you can address them by the only identity the engine cares about.
        const uint hash = 0x4A724578;
        byte[] content = "raw bytes"u8.ToArray();

        PatchBuilder.Build(_install, [MakeZipMod("m", ($"mods/_hash/{hash:x8}.xbt", content))]);

        var index = FatArchive.Read(_install.PatchFat);
        Assert.Contains(index.Entries, e => e.Hash == hash);
    }

    [Fact]
    public void Later_mods_win_over_earlier_ones()
    {
        if (_install is null) return;

        const string path = "engine/gamemodes/gamemodesconfig.xml";
        var first = MakeZipMod("first", ($"mods/{path}", "first"u8.ToArray()));
        var second = MakeZipMod("second", ($"mods/{path}", "second"u8.ToArray()));

        PatchBuilder.Build(_install, [first, second]);

        var index = FatArchive.Read(_install.PatchFat);
        var entry = index.Entries.First(e => e.Hash == NameHash.Compute(path));

        using var dat = File.OpenRead(_install.PatchDat);
        var actual = new byte[entry.StoredSize];
        dat.Seek(entry.Offset, SeekOrigin.Begin);
        dat.ReadExactly(actual);

        Assert.Equal("second"u8.ToArray(), actual);
    }

    [Fact]
    public void A_failed_build_leaves_the_existing_patch_untouched()
    {
        if (_install is null) return;

        byte[] before = File.ReadAllBytes(_install.PatchDat);

        var broken = new ThrowingModLayer();
        Assert.ThrowsAny<Exception>(() => PatchBuilder.Build(_install, [broken]));

        // A half-written patch.dat is a broken game install, so failure must be inert.
        Assert.Equal(before, File.ReadAllBytes(_install.PatchDat));
        Assert.False(File.Exists(_install.PatchDat + ".building"));
    }

    [Fact]
    public void A_fragment_override_is_assembled_into_its_container_rather_than_dropped()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        VfsFile container;
        string fragmentId;
        int vanillaFragmentCount;
        using (var vfs = GameVfs.Load(_install, names))
        {
            // A named container specifically: its own real path hashes back to its own hash, so
            // staging at "container's path + fragment id" (the normal, no-_hash\-needed case) works.
            VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            fragmentId = fragment.FragmentId!;

            FcbObject baseTree = FcbDocument.Deserialize(vfs.ReadOriginal(container.Hash)!);
            vanillaFragmentCount = FcbFragments.List(baseTree).Count;
        }

        // A hand-built replacement, unrelated to the original content - before fragment overlays,
        // this path would have hashed as one opaque whole-file override under a synthetic hash that
        // means nothing to the engine, and the container's own hash would have copied through from
        // vanilla completely untouched. If that regressed, the rebuilt container just wouldn't
        // contain this value anywhere.
        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB };
        replacement.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        string replacementXml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);

        string fragmentPath = $"{container.Path}\\{fragmentId}";
        var mod = MakeZipMod("fragment_mod", ($"mods/{fragmentPath}", System.Text.Encoding.UTF8.GetBytes(replacementXml)));

        using (var vfsForRead = GameVfs.Load(_install, names))
        {
            PatchBuilder.Build(_install, [mod], vfsForRead.ReadOriginal);
        }

        var patchIndex = FatArchive.Read(_install.PatchFat);
        var entry = patchIndex.Entries.First(e => e.Hash == container.Hash);
        using var rebuilt = DuniaArchive.Open(_install.PatchFat);
        FcbObject rebuiltContainer = FcbDocument.Deserialize(rebuilt.Read(entry));

        // Located by its marker value, not by re-looking-up the old id string: a fragment's id
        // derives from its own content, and the hand-built replacement above carries no identity, so
        // the replaced slot's id ceases to exist - that's correct, content-derived behavior. The
        // fragment count dropping by exactly one pins that it replaced in place rather than appended.
        FcbObject? spliced = TestSupport.FindNodeWithValue(rebuiltContainer, 0xDEADBEEF);
        Assert.NotNull(spliced);
        Assert.Equal([0x2A, 0x00, 0x00, 0x00], spliced.Values[0xDEADBEEF]);
        Assert.Equal(vanillaFragmentCount - 1, FcbFragments.List(rebuiltContainer).Count);
    }

    /// <summary>
    /// A staged fragment id that matches nothing in the vanilla container isn't a stale override - it's
    /// a mod adding a genuinely new entity (e.g. a new vehicle) that never existed in vanilla to begin
    /// with. Covers the same path <see cref="FcbAssemblerTests"/> exercises directly, but end to end
    /// through a real build: <see cref="FragmentMerge.Resolve"/>'s empty-ancestor handling feeding
    /// <see cref="FcbAssembler.Apply"/>'s append.
    /// </summary>
    [Fact]
    public void A_fragment_id_with_no_vanilla_match_is_added_to_the_container_instead_of_erroring()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        VfsFile container;
        int originalChildCount;
        using (var vfs = GameVfs.Load(_install, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            originalChildCount = FcbDocument.Deserialize(vfs.ReadOriginal(container.Hash)!).Children.Count;
        }

        var addition = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        addition.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        string additionXml = FcbXml.ToXml(addition, FcbClassDefinitions.Empty);

        string newFragmentPath = $"{container.Path}\\does_not_exist_in_vanilla.xml";
        var mod = MakeZipMod("add_mod", ($"mods/{newFragmentPath}", System.Text.Encoding.UTF8.GetBytes(additionXml)));

        using (var vfsForRead = GameVfs.Load(_install, names))
        {
            PatchBuilder.Build(_install, [mod], vfsForRead.ReadOriginal);
        }

        var patchIndex = FatArchive.Read(_install.PatchFat);
        var entry = patchIndex.Entries.First(e => e.Hash == container.Hash);
        using var rebuilt = DuniaArchive.Open(_install.PatchFat);
        FcbObject rebuiltContainer = FcbDocument.Deserialize(rebuilt.Read(entry));

        Assert.Equal(originalChildCount + 1, rebuiltContainer.Children.Count);
        FcbObject added = rebuiltContainer.Children[^1];
        Assert.Equal(0xE0BDB3DBu, added.TypeHash);
        Assert.Equal([0x2A, 0x00, 0x00, 0x00], added.Values[0xDEADBEEF]);
    }

    /// <summary>
    /// Milestone 3 (docs/design/fcb-fragment-overlays.md): two mods editing different, non-adjacent
    /// parts of the same fragment both survive in the built patch, instead of the second one silently
    /// clobbering the first's edit outright (Milestone 2's "last layer wins" rule for fragments).
    /// </summary>
    [Fact]
    public void Two_mods_editing_different_children_of_the_same_fragment_both_survive_the_build()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        VfsFile container;
        string fragmentId;
        FcbObject vanillaFragment;
        using (var vfs = GameVfs.Load(_install, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            fragmentId = fragment.FragmentId!;

            FcbObject baseTree = FcbDocument.Deserialize(vfs.ReadOriginal(container.Hash)!);
            vanillaFragment = FcbFragments.Find(baseTree, fragmentId)!;
        }
        if (TestSupport.TwoDistantEditPaths(vanillaFragment) is not { } paths)
        {
            return; // fixture too small to prove non-overlapping edits safely
        }

        string fragmentPath = $"{container.Path}\\{fragmentId}";
        var modA = MakeZipMod("fragment_mod_a",
            ($"mods/{fragmentPath}", TestSupport.RenderWithValueSetAt(vanillaFragment, paths.A, 0xAAAA0001, [0x01, 0x00, 0x00, 0x00])));
        var modB = MakeZipMod("fragment_mod_b",
            ($"mods/{fragmentPath}", TestSupport.RenderWithValueSetAt(vanillaFragment, paths.B, 0xAAAA0002, [0x02, 0x00, 0x00, 0x00])));

        using (var vfsForRead = GameVfs.Load(_install, names))
        {
            PatchBuilder.Build(_install, [modA, modB], vfsForRead.ReadOriginal, FcbClassDefinitions.Empty);
        }

        var patchIndex = FatArchive.Read(_install.PatchFat);
        var entry = patchIndex.Entries.First(e => e.Hash == container.Hash);
        using var rebuilt = DuniaArchive.Open(_install.PatchFat);
        FcbObject rebuiltContainer = FcbDocument.Deserialize(rebuilt.Read(entry));

        // Both edits preserve the fragment's identity fields, so the same id resolves in the rebuilt tree.
        FcbObject rebuiltFragment = FcbFragments.Find(rebuiltContainer, fragmentId)!;
        Assert.Equal([0x01, 0x00, 0x00, 0x00], TestSupport.NodeAt(rebuiltFragment, paths.A).Values[0xAAAA0001]);
        Assert.Equal([0x02, 0x00, 0x00, 0x00], TestSupport.NodeAt(rebuiltFragment, paths.B).Values[0xAAAA0002]);
    }

    /// <summary>
    /// <c>jackall-cli mod build</c> is the one caller with nobody to ask when two mods genuinely
    /// collide inside the same fragment - unlike JackAll.App, which has an interactive row to hand-fix
    /// one on (<see cref="GameVfsFragmentOverrideTests.Two_mods_editing_the_same_field_differently_throws_a_conflict_naming_the_mod"/>
    /// pins that throw). <see cref="PatchBuilder.Build"/>'s <c>resolveFragmentConflictsWithLoadOrder</c>
    /// makes the build succeed anyway - the higher-priority layer's edit wins outright, same as a
    /// whole-file override - and records what happened on <see cref="BuildResult.Conflicts"/> instead.
    /// </summary>
    [Fact]
    public void Lenient_mode_resolves_a_genuine_fragment_conflict_with_load_order_and_reports_it()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        VfsFile container;
        string fragmentId;
        FcbObject vanillaFragment;
        using (var vfs = GameVfs.Load(_install, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            fragmentId = fragment.FragmentId!;

            FcbObject baseTree = FcbDocument.Deserialize(vfs.ReadOriginal(container.Hash)!);
            vanillaFragment = FcbFragments.Find(baseTree, fragmentId)!;
        }

        // Same existing field, different content from both mods - a genuine collision, not two
        // independent, non-overlapping edits (which Milestone 3 already merges cleanly). A prototype
        // fragment's own value table can be empty - its Entity child's never is. Identity fields are
        // excluded so the fragment's id survives the garbage value and stays resolvable below.
        int[] targetPath = vanillaFragment.Values.Count > 0 ? [] : [0];
        FcbObject target = TestSupport.NodeAt(vanillaFragment, targetPath);
        uint existingHash = target.Values.Keys.FirstOrDefault(
            k => k != WorldHashes.HidName && k != WorldHashes.DisEntityId);
        if (existingHash == 0) return; // fixture has nothing existing to collide on
        string fragmentPath = $"{container.Path}\\{fragmentId}";
        var modA = MakeZipMod("mod_a",
            ($"mods/{fragmentPath}", TestSupport.RenderWithValueSetAt(vanillaFragment, targetPath, existingHash, [0x01, 0x00, 0x00, 0x00])));
        var modB = MakeZipMod("mod_b",
            ($"mods/{fragmentPath}", TestSupport.RenderWithValueSetAt(vanillaFragment, targetPath, existingHash, [0x02, 0x00, 0x00, 0x00])));

        BuildResult result;
        using (var vfsForRead = GameVfs.Load(_install, names))
        {
            result = PatchBuilder.Build(_install, [modA, modB], vfsForRead.ReadOriginal,
                resolveFragmentConflictsWithLoadOrder: true);
        }

        FragmentConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal(fragmentId, conflict.FragmentId, ignoreCase: true);
        // The id alone names one entity of one container; the report has to say which container.
        Assert.Equal(container.Path, conflict.Container, ignoreCase: true);
        Assert.Equal("mod_b", conflict.WinningLayer);
        Assert.Contains("mod_a", conflict.EarlierLayers);

        var patchIndex = FatArchive.Read(_install.PatchFat);
        var entry = patchIndex.Entries.First(e => e.Hash == container.Hash);
        using var rebuilt = DuniaArchive.Open(_install.PatchFat);
        FcbObject rebuiltContainer = FcbDocument.Deserialize(rebuilt.Read(entry));

        // mod_b (later in the layer list, so higher priority) won outright - not some Diff3 partial
        // merge of the two, and not mod_a's value either.
        FcbObject rebuiltFragment = FcbFragments.Find(rebuiltContainer, fragmentId)!;
        Assert.Equal([0x02, 0x00, 0x00, 0x00], TestSupport.NodeAt(rebuiltFragment, targetPath).Values[existingHash]);
    }

    /// <summary>A layer that addressed the container by hash has no path to recover a name from, so
    /// the conflict reports the same synthetic spelling the override was staged under.</summary>
    [Fact]
    public void A_conflict_in_a_hash_addressed_container_reports_the_synthetic_container_path()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        VfsFile container;
        string fragmentId;
        FcbObject vanillaFragment;
        using (var vfs = GameVfs.Load(_install, names))
        {
            VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
            container = vfs.Files[fragment.ContainerHash!.Value];
            fragmentId = fragment.FragmentId!;
            vanillaFragment = FcbFragments.Find(
                FcbDocument.Deserialize(vfs.ReadOriginal(container.Hash)!), fragmentId)!;
        }

        int[] targetPath = vanillaFragment.Values.Count > 0 ? [] : [0];
        uint existingHash = TestSupport.NodeAt(vanillaFragment, targetPath).Values.Keys.FirstOrDefault(
            k => k != WorldHashes.HidName && k != WorldHashes.DisEntityId);
        if (existingHash == 0) return; // fixture has nothing existing to collide on

        string hashAddressedPath = $"_hash\\{container.Hash:x8}.fcb\\{fragmentId}";
        var modA = MakeZipMod("mod_a", ($"mods/{hashAddressedPath}",
            TestSupport.RenderWithValueSetAt(vanillaFragment, targetPath, existingHash, [0x01, 0x00, 0x00, 0x00])));
        var modB = MakeZipMod("mod_b", ($"mods/{hashAddressedPath}",
            TestSupport.RenderWithValueSetAt(vanillaFragment, targetPath, existingHash, [0x02, 0x00, 0x00, 0x00])));

        BuildResult result;
        using (var vfsForRead = GameVfs.Load(_install, names))
        {
            result = PatchBuilder.Build(_install, [modA, modB], vfsForRead.ReadOriginal,
                resolveFragmentConflictsWithLoadOrder: true);
        }

        FragmentConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal($"_hash\\{container.Hash:x8}.fcb", conflict.Container);
    }

    [Fact]
    public void A_GameVfs_kept_open_across_two_builds_can_still_read_the_patch_archive_afterward()
    {
        if (_install is null) return;

        // Mirrors MainViewModel: one GameVfs, loaded once, never disposed between builds - exactly the
        // shape that crashed in practice with EndOfStreamException (Windows NTFS quirk: once
        // PatchBuilder.Build replaces patch.dat/.fat, this GameVfs's own "patch" DuniaArchive handle
        // isn't just stale, it throws on the next read landing there - see DuniaArchive.Open's remarks).
        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        VfsFile container = vfs.Files[fragment.ContainerHash!.Value];

        // First build: no mods, just replaces patch.dat/.fat once so the "GameVfs's archive handle
        // needs re-opening" condition actually exists before the real test below.
        PatchBuilder.Build(_install, [], vfs.ReadOriginal);
        vfs.ReloadPatchArchive();

        // Second build: a fragment override whose container lives in "patch" - the only archive this
        // fixture mounts, so readArchiveOriginal has no choice but to read through the handle the
        // first build just invalidated. Without ReloadPatchArchive above, this throws.
        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB };
        replacement.Values.Add(0xDEADBEEF, [0x01, 0x02, 0x03, 0x04]);
        string xml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);
        string fragmentPath = $"{container.Path}\\{fragment.FragmentId}";
        var mod = MakeZipMod("fragment_mod_2", ($"mods/{fragmentPath}", System.Text.Encoding.UTF8.GetBytes(xml)));

        BuildResult result = PatchBuilder.Build(_install, [mod], vfs.ReadOriginal);
        Assert.True(result.TotalEntries > 0);
    }

    /// <summary>
    /// <see cref="GameVfs.ReadOriginal"/> has to stay the true vanilla ancestor even for a container
    /// whose only archive-provided home is "patch" - the one archive a deploy actually rewrites.
    /// Before <see cref="GameVfs.ReadOriginal"/> started preferring <c>install.VanillaPatchFat</c>/
    /// <c>.Dat</c>, a second deploy's fragment merge (and, in JackAll.App, the XML editor's "differs
    /// from vanilla" highlight) would silently diff against the *first* deploy's own output instead -
    /// so editing the exact same field twice would stop showing as changed the second time, and a
    /// three-way fragment merge across two mods sharing that container would use the wrong ancestor.
    /// </summary>
    [Fact]
    public void ReadOriginal_still_returns_true_vanilla_for_a_patch_resident_container_after_a_deploy()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        VfsFile container = vfs.Files[fragment.ContainerHash!.Value];
        byte[] trueVanillaContainer = vfs.ReadOriginal(container.Hash)!;

        // A real, content-changing deploy this time (unlike the empty-mods build above) - this is what
        // actually reproduces the bug: the live patch.dat on disk now differs from vanilla for this
        // exact hash.
        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB };
        replacement.Values.Add(0xDEADBEEF, [0xAA, 0xBB, 0xCC, 0xDD]);
        string xml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);
        string fragmentPath = $"{container.Path}\\{fragment.FragmentId}";
        var mod = MakeZipMod("fragment_mod_vanilla_check", ($"mods/{fragmentPath}", System.Text.Encoding.UTF8.GetBytes(xml)));

        PatchBuilder.Build(_install, [mod], vfs.ReadOriginal);
        vfs.ReloadPatchArchive();

        // Sanity check that the deploy actually changed this container - otherwise the assertion below
        // would trivially pass without exercising the bug at all.
        Assert.NotEqual(trueVanillaContainer, vfs.Read(container.Hash));

        Assert.Equal(trueVanillaContainer, vfs.ReadOriginal(container.Hash));
    }

    [Fact]
    public void Staging_a_fragment_override_for_an_already_memoized_noncacheable_container_does_not_crash()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();

        // GameVfs.Load's default includeFragments:true runs GameVfs.MergeFragments once, with no
        // layers - this memoizes every .fcb container's fragment structure (see GameVfs._fragmentMemo)
        // *without* any override in play, including this fixture's own containers, which all live in
        // "patch" - the one archive GameVfs never writes to its persisted GameCache (see
        // GameVfs._archiveIsVolatile), because this fixture's constructor mounts the fixture directly
        // as install.PatchFat/.Dat.
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        VfsFile container = vfs.Files[fragment.ContainerHash!.Value];
        Assert.Equal("patch", container.SourceName); // sanity: confirms the non-cacheable setup above.

        string workspaceDir = Path.Combine(_sandbox, "workspace");
        Directory.CreateDirectory(workspaceDir);
        var workspace = new FolderModLayer(workspaceDir, "workspace");

        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB };
        replacement.Values.Add(0xDEADBEEF, [0x09, 0x08, 0x07, 0x06]);
        string xml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);
        workspace.Stage(fragment.Hash, fragment.Path, "xml", System.Text.Encoding.UTF8.GetBytes(xml));

        // Before the fix: KeyNotFoundException. MergeFragments' pass 1 skipped this container via a
        // stale-but-still-matching memo hit (its SourceKind/SourceName never changed), while pass 3 is
        // forced past its own memo shortcut the moment hasOverrides flips to true for it - and finds
        // neither GameCache (never populated - not cacheable) nor the `uncached` dictionary (pass 1
        // never queued it) has an entry for it.
        vfs.Rebuild([workspace]);

        VfsFile overridden = vfs.Files[fragment.Hash];
        Assert.True(overridden.IsOverriding);
        Assert.Equal(xml, System.Text.Encoding.UTF8.GetString(vfs.Read(fragment.Hash)));
    }

    [Fact]
    public void A_plugins_only_layer_leaves_the_archive_vanilla_and_populates_bin_plugins()
    {
        if (_install is null) return;

        byte[] originalDat = File.ReadAllBytes(_install.PatchDat);
        string deployed = Path.Combine(_sandbox, "bin", "plugins", "foo.dll");
        var mod = MakeZipMod("p", ("plugins/foo.dll", "plugin bytes"u8.ToArray()));

        var result = PatchBuilder.Build(_install, [mod]);

        // The load-bearing half: nothing under plugins\ may leak into the archive.
        Assert.Equal(originalDat, File.ReadAllBytes(_install.PatchDat));
        Assert.Equal(0, result.AddedEntries);
        Assert.Equal(1, result.Plugins.Deployed);
        Assert.True(File.Exists(deployed));

        mod.Enabled = false;
        Assert.Equal(1, PatchBuilder.Build(_install, [mod]).Plugins.Removed);
        Assert.False(File.Exists(deployed));
    }

    [Fact]
    public void Content_outside_the_mods_folder_contributes_nothing()
    {
        if (_install is null) return;

        byte[] vanilla = File.ReadAllBytes(_install.PatchDat);
        PatchBuilder.Build(_install,
            [MakeZipMod("root_layout", ("engine/gamemodes/gamemodesconfig.xml", "hello"u8.ToArray()))]);

        // The old root layout is gone, not grandfathered: a layer without mods\/plugins\ is empty.
        Assert.Equal(vanilla, File.ReadAllBytes(_install.PatchDat));
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

    private sealed class ThrowingModLayer : IModLayer
    {
        public string Name => "broken";
        public bool Enabled { get; set; } = true;
        public IReadOnlyCollection<uint> Hashes => [0xDEADBEEF];
        public byte[] Read(uint hash) => throw new IOException("simulated read failure");
        public string? PathOf(uint hash) => null;
        public IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; } =
            new Dictionary<uint, IReadOnlyList<FragmentOverride>>();
        public IReadOnlyCollection<string> PluginPaths => [];
        public byte[] ReadPlugin(string pluginPath) => throw new KeyNotFoundException();
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
