using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Tests;

/// <summary>
/// Milestone 2 of docs/design/fcb-fragment-overlays.md: a fragment row (Milestone 1's read-only
/// browsing) becomes a real, stageable override, composed into its container instead of requiring a
/// whole-file replacement. These tests cover the <see cref="GameVfs"/> side specifically — display
/// attribution and <see cref="GameVfs.Read"/> — leaving the on-disk build itself to
/// <c>PatchBuilderTests</c>.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class GameVfsFragmentOverrideTests : IDisposable
{
    private const string FixturesDir = "Fixtures/Patch";

    /// <summary>
    /// A fragment row's structural parent, which its id deliberately does not record - so the file
    /// browser can show it rather than leaving the user to guess.
    /// </summary>
    [Fact]
    public void A_fragment_row_reports_the_group_it_lives_in()
    {
        if (_install is null) return;

        using var vfs = GameVfs.Load(_install, TestSupport.LoadNames());
        vfs.LoadFragments();

        VfsFile row = vfs.Files.Values.First(TestSupport.IsFcbFragment);
        FragmentAncestry ancestry = Assert.IsType<FragmentAncestry>(vfs.AncestryOf(row));

        Assert.NotEmpty(ancestry.ParentName);
        Assert.NotEmpty(ancestry.Display);

        // Asked twice, the memo answers - and answers the same thing.
        Assert.Equal(ancestry, vfs.AncestryOf(row));

        // A container is not a fragment and has no parent of this kind.
        Assert.Null(vfs.AncestryOf(vfs.Files[row.ContainerHash!.Value]));
    }

    /// <summary>
    /// The file browser has to show the same entries a mod stages, under ids that match - otherwise a
    /// staged override looks like unrelated new content and the same resource appears twice.
    /// </summary>
    /// <remarks>
    /// This is the case that actually broke. The two sides do not know the same names: the app labels
    /// a resource from the hashlist, which carries no animation packages at all, while a mod author
    /// writing <c>dragunov</c> does know one. Because the id binds on its number and the label is
    /// decoration, the app's bare-number row and the mod's labelled file are one entry.
    /// </remarks>
    [Fact]
    public void A_depload_row_and_a_differently_labelled_staged_file_are_one_entry()
    {
        if (_install is null) return;

        using var vfs = GameVfs.Load(_install, TestSupport.LoadNames());
        vfs.LoadFragments();

        VfsFile? row = vfs.Files.Values.FirstOrDefault(TestSupport.IsDepLoadFragment);
        Assert.NotNull(row);

        VfsFile container = vfs.Files[row!.ContainerHash!.Value];
        int rowsBefore = vfs.Files.Values.Count(f => f.ContainerHash == row.ContainerHash);

        // What a mod author would write: the same resource, labelled however they know it.
        DepLoadParent parent = DepLoadXml.FragmentFromXml(System.Text.Encoding.UTF8.GetString(vfs.Read(row.Hash)));
        string labelled = DepLoadContainerSplitter.IdOf(parent.Hash, "what_a_modder_calls_it");
        Assert.NotEqual(row.FragmentId, labelled);

        DepLoadParent edited = parent with
        {
            Children = [.. parent.Children, new DepLoadChild(0x11641D75, 0xB0604725)],
        };
        string stagedPath = container.Path + "\\" + labelled;

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(NameHash.Compute(stagedPath), stagedPath, "xml",
            System.Text.Encoding.UTF8.GetBytes(DepLoadXml.FragmentToXml(edited)));
        vfs.Rebuild([workspace]);

        // One entry, not two: the existing row is the override, and nothing was added beside it.
        Assert.Equal(rowsBefore, vfs.Files.Values.Count(f => f.ContainerHash == row.ContainerHash));
        VfsFile after = vfs.Files[row.Hash];
        Assert.True(after.IsModded);
        Assert.True(after.IsOverriding);
        Assert.Equal("workspace", after.SourceName);
        Assert.Contains("291773813", System.Text.Encoding.UTF8.GetString(vfs.Read(row.Hash)), StringComparison.Ordinal);

        // And the container assembles it in, so a build would carry the edit.
        Assert.Equal("workspace", vfs.Files[container.Hash].FragmentOverrideSource);
        Assert.NotEqual(vfs.ReadOriginal((uint)container.Hash), vfs.Read(container.Hash));
    }

    private readonly string _sandbox;
    private readonly string _workspaceDir;
    private readonly GameInstall? _install;

    public GameVfsFragmentOverrideTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "jackall-vfs-fragment", Guid.NewGuid().ToString("N"));
        _workspaceDir = Path.Combine(_sandbox, "workspace");
        Directory.CreateDirectory(_workspaceDir);

        string fixtureFat = Path.Combine(FixturesDir, "patch.fat");
        string fixtureDat = Path.Combine(FixturesDir, "patch.dat");
        if (!File.Exists(fixtureFat) || !File.Exists(fixtureDat))
        {
            return;
        }

        // Mounted under a name other than "patch" - GameVfs treats install.PatchFat as the volatile,
        // never-cached archive, and this suite wants the ordinary cacheable path. patch.fat/.dat still
        // need to exist to satisfy GameInstall.TryOpen; GameVfs skips them since they fail to parse.
        Directory.CreateDirectory(Path.Combine(_sandbox, "bin"));
        Directory.CreateDirectory(Path.Combine(_sandbox, "Data_Win32"));
        File.WriteAllText(Path.Combine(_sandbox, "bin", "FarCry2.exe"), "stub");
        File.WriteAllText(Path.Combine(_sandbox, "Data_Win32", "patch.fat"), "not a real archive");
        File.WriteAllText(Path.Combine(_sandbox, "Data_Win32", "patch.dat"), "not a real archive");
        File.Copy(fixtureFat, Path.Combine(_sandbox, "Data_Win32", "common.fat"));
        File.Copy(fixtureDat, Path.Combine(_sandbox, "Data_Win32", "common.dat"));

        _install = GameInstall.TryOpen(_sandbox, out _);
    }

    private static byte[] BuildReplacementFragmentXml()
    {
        var replacement = new FcbObject { TypeHash = 0xE0BDB3DB }; // EntityLibraryGroup
        replacement.Values.Add(0xDEADBEEF, [0x2A, 0x00, 0x00, 0x00]);
        string xml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty);
        return System.Text.Encoding.UTF8.GetBytes(xml);
    }

    /// <summary>Covers <see cref="GameVfs.ReadOriginalFragment"/> directly - it drives JackAll.App's
    /// XML editor "differs from vanilla" highlight, but had no test of its own before this.</summary>
    [Fact]
    public void ReadOriginalFragment_returns_the_pre_staging_content_even_after_a_fragment_is_overridden()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
        byte[] originalFragmentContent = vfs.Read(fragment.Hash);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] replacement = BuildReplacementFragmentXml();
        workspace.Stage(NameHash.Compute(fragment.Path), fragment.Path, "xml", replacement);
        vfs.Rebuild([workspace]);

        string? original = vfs.ReadOriginalFragment(fragment.ContainerHash!.Value, fragment.FragmentId!);
        Assert.NotNull(original);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(originalFragmentContent).TrimStart('﻿'), original.TrimStart('﻿'));
        Assert.NotEqual(System.Text.Encoding.UTF8.GetString(replacement), original);
    }

    [Fact]
    public void Staging_a_fragment_override_updates_its_own_row_its_container_and_leaves_siblings_alone()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        // A named container specifically: its own real path hashes back to its own hash, so staging
        // at "container's path + fragment id" (the normal, no-_hash\-needed case) works. The unnamed
        // case (which needs the deeper _hash\<hex>.fcb\<fragment id> convention instead) is covered
        // separately below.
        VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
        VfsFile containerBefore = vfs.Files[fragment.ContainerHash!.Value];
        VfsFile? sibling = vfs.Files.Values.FirstOrDefault(
            f => TestSupport.IsFcbFragment(f) && f.ContainerHash == fragment.ContainerHash && f.Hash != fragment.Hash);
        byte[] originalFragmentContent = vfs.Read(fragment.Hash);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] replacement = BuildReplacementFragmentXml();
        workspace.Stage(NameHash.Compute(fragment.Path), fragment.Path, "xml", replacement);
        vfs.Rebuild([workspace]);

        // The fragment row itself: modded, attributed to the workspace, reads back the override.
        VfsFile overriddenFragment = vfs.Files[fragment.Hash];
        Assert.True(overriddenFragment.IsModded);
        Assert.True(overriddenFragment.IsOverriding);
        Assert.Equal("workspace", overriddenFragment.SourceName);
        Assert.Equal(SourceKind.Mod, overriddenFragment.SourceKind);
        Assert.Equal(replacement, vfs.Read(fragment.Hash));
        Assert.NotEqual(originalFragmentContent, vfs.Read(fragment.Hash));

        // The container's own row: modded via FragmentOverrideSource, but its *whole-file* resolution
        // (SourceKind/SourceName) is untouched - the workspace never staged a whole-file replacement.
        VfsFile overriddenContainer = vfs.Files[containerBefore.Hash];
        Assert.True(overriddenContainer.IsModded);
        Assert.Equal("workspace", overriddenContainer.FragmentOverrideSource);
        Assert.Equal(containerBefore.SourceKind, overriddenContainer.SourceKind);
        Assert.Equal(containerBefore.SourceName, overriddenContainer.SourceName);
        Assert.False(overriddenContainer.IsOverriding);

        // Reading the container assembles the override in - different from the untouched archive copy.
        byte[] assembledContainer = vfs.Read(containerBefore.Hash);
        Assert.NotEqual(vfs.ReadOriginal((uint)containerBefore.Hash), assembledContainer);

        // An un-overridden sibling fragment is completely unaffected.
        if (sibling is not null)
        {
            VfsFile stillPlain = vfs.Files[sibling.Hash];
            Assert.False(stillPlain.IsOverriding);
            Assert.False(stillPlain.IsModded);
            Assert.Equal(SourceKind.Archive, stillPlain.SourceKind);
        }

        // Reverting removes the override from both the fragment row and the container's attribution.
        // The layer's own key is the staged path re-hashed, never the row's synthetic VFS key.
        Assert.True(workspace.Unstage(NameHash.Compute(fragment.Path)));
        vfs.Rebuild([workspace]);

        Assert.False(vfs.Files[fragment.Hash].IsOverriding);
        Assert.Equal(originalFragmentContent, vfs.Read(fragment.Hash));
        Assert.Null(vfs.Files[containerBefore.Hash].FragmentOverrideSource);
    }

    /// <summary>
    /// A staged fragment id with no vanilla match (a mod adding a new entity, not overriding an
    /// existing one) gets its own synthetic row in the VFS - not just spliced silently into the
    /// container's bytes - so it's browsable/readable like any other fragment.
    /// </summary>
    [Fact]
    public void Staging_a_brand_new_fragment_id_adds_its_own_browsable_row()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile existingFragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
        VfsFile container = vfs.Files[existingFragment.ContainerHash!.Value];
        int fragmentRowCountBefore = vfs.Files.Values.Count(f => f.ContainerHash == container.Hash);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] addition = BuildReplacementFragmentXml();
        string newFragmentPath = $"{container.Path}\\does_not_exist_in_vanilla.xml";
        workspace.Stage(NameHash.Compute(newFragmentPath), newFragmentPath, "xml", addition);
        vfs.Rebuild([workspace]);

        Assert.Equal(fragmentRowCountBefore + 1, vfs.Files.Values.Count(f => f.ContainerHash == container.Hash));

        // Found by identity, not by hashing its path - a fragment row's key is synthetic.
        VfsFile added = vfs.Files.Values.Single(f =>
            f.ContainerHash == container.Hash
            && FcbFragments.IdComparer.Equals(f.FragmentId, "does_not_exist_in_vanilla.xml"));
        Assert.True(added.IsFragment);
        Assert.True(added.IsModded);
        Assert.False(added.IsOverriding); // not overriding an existing child - there wasn't one
        Assert.Equal("workspace", added.SourceName);
        Assert.Equal(addition, vfs.Read(added.Hash));

        // The existing sibling fragment (and every other) is unaffected.
        Assert.False(vfs.Files[existingFragment.Hash].IsModded);
    }

    [Fact]
    public void A_fragment_can_be_addressed_via_the_hash_folder_instead_of_its_containers_own_path()
    {
        if (_install is null) return;

        // Proves the _hash\<container hash>.fcb\<fragment id> convention itself resolves to the right
        // container, independent of whether this particular fixture happens to contain an unnamed
        // splitting .fcb - it's the only way to address a fragment inside a container whose own name
        // isn't known, since an unnamed container's row uses GameVfs.SyntheticPath, which (unlike a
        // named container's real recovered path) deliberately doesn't hash back to the real archive
        // hash. A named container's fragment works the same way regardless, so any fragment proves it.
        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(TestSupport.IsFcbFragment);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] replacement = BuildReplacementFragmentXml();
        string hashAddressedPath = $"_hash\\{fragment.ContainerHash:x8}.fcb\\{fragment.FragmentId}";
        workspace.Stage(NameHash.Compute(hashAddressedPath), hashAddressedPath, "xml", replacement);
        vfs.Rebuild([workspace]);

        VfsFile overriddenFragment = vfs.Files[fragment.Hash];
        Assert.True(overriddenFragment.IsOverriding);
        Assert.Equal("workspace", overriddenFragment.SourceName);
        Assert.Equal(replacement, vfs.Read(fragment.Hash));
        Assert.Equal("workspace", vfs.Files[fragment.ContainerHash!.Value].FragmentOverrideSource);
    }

    /// <summary>The real vanilla <see cref="FcbObject"/> a fragment row's id refers to — the actual
    /// ancestor Milestone 3's merge diffs against, as opposed to <see cref="BuildReplacementFragmentXml"/>'s
    /// synthetic, unrelated-to-vanilla replacement (fine for a single-layer override, since with only
    /// one contributing layer the merge is a proven no-op pass-through, but a real multi-layer merge
    /// test needs edits that actually derive from the same ancestor to mean anything).</summary>
    private static FcbObject VanillaFragmentObject(GameVfs vfs, VfsFile fragment)
        => FcbFragments.Find(
            FcbDocument.Deserialize(vfs.ReadOriginal(fragment.ContainerHash!.Value)!), fragment.FragmentId!)!;

    [Fact]
    public void Two_mods_editing_different_fields_of_the_same_fragment_both_survive_in_the_merged_read()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
        FcbObject vanilla = VanillaFragmentObject(vfs, fragment);
        if (TestSupport.TwoDistantEditPaths(vanilla) is not { } paths)
        {
            return; // fixture too small to prove non-overlapping edits safely
        }

        var zipDir = Path.Combine(_sandbox, "zip_src");
        Directory.CreateDirectory(zipDir);
        string zipEntryPath = Path.Combine(zipDir, "mods", fragment.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(zipEntryPath)!);
        File.WriteAllBytes(zipEntryPath, TestSupport.RenderWithValueSetAt(vanilla, paths.A, 0xAAAA0001, [0x01, 0x00, 0x00, 0x00]));
        string zipPath = Path.Combine(_sandbox, "mod_a.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(zipDir, zipPath);
        var zipMod = new ZipModLayer(zipPath);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(NameHash.Compute(fragment.Path), fragment.Path, "xml", TestSupport.RenderWithValueSetAt(vanilla, paths.B, 0xAAAA0002, [0x02, 0x00, 0x00, 0x00]));

        vfs.Rebuild([zipMod, workspace]);

        VfsFile mergedFragment = vfs.Files[fragment.Hash];
        Assert.True(mergedFragment.IsModded);
        Assert.Equal("multiple mods", mergedFragment.SourceName);

        FcbObject merged = FcbXml.FromXml(System.Text.Encoding.UTF8.GetString(vfs.Read(fragment.Hash)));
        Assert.Equal([0x01, 0x00, 0x00, 0x00], TestSupport.NodeAt(merged, paths.A).Values[0xAAAA0001]);
        Assert.Equal([0x02, 0x00, 0x00, 0x00], TestSupport.NodeAt(merged, paths.B).Values[0xAAAA0002]);

        // The container composes both edits too, not just the standalone fragment row. Both edits
        // preserve the fragment's identity fields, so the same id still resolves in the composed tree.
        FcbObject container = FcbDocument.Deserialize(vfs.Read(fragment.ContainerHash!.Value));
        FcbObject composed = FcbFragments.Find(container, fragment.FragmentId!)!;
        Assert.Equal([0x01, 0x00, 0x00, 0x00], TestSupport.NodeAt(composed, paths.A).Values[0xAAAA0001]);
        Assert.Equal([0x02, 0x00, 0x00, 0x00], TestSupport.NodeAt(composed, paths.B).Values[0xAAAA0002]);
    }

    [Fact]
    public void Two_mods_editing_the_same_field_differently_throws_a_conflict_naming_the_mod()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
        FcbObject vanilla = VanillaFragmentObject(vfs, fragment);
        // A prototype fragment's own value table can be empty - its Entity child's never is.
        int[] targetPath = vanilla.Values.Count > 0 ? [] : [0];
        FcbObject target = TestSupport.NodeAt(vanilla, targetPath);
        if (target.Values.Count == 0) return; // fixture has nothing existing to collide on

        uint existingHash = target.Values.Keys.First();

        var zipDir = Path.Combine(_sandbox, "zip_src_conflict");
        Directory.CreateDirectory(zipDir);
        string zipEntryPath = Path.Combine(zipDir, "mods", fragment.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(zipEntryPath)!);
        // Same existing field, different content - a genuine collision, not two independent adds.
        File.WriteAllBytes(zipEntryPath, TestSupport.RenderWithValueSetAt(vanilla, targetPath, existingHash, [0x01, 0x00, 0x00, 0x00]));
        string zipPath = Path.Combine(_sandbox, "mod_conflict.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(zipDir, zipPath);
        var zipMod = new ZipModLayer(zipPath);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(NameHash.Compute(fragment.Path), fragment.Path, "xml", TestSupport.RenderWithValueSetAt(vanilla, targetPath, existingHash, [0xFF, 0x00, 0x00, 0x00]));

        vfs.Rebuild([zipMod, workspace]);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => vfs.Read(fragment.Hash));
        Assert.Contains("workspace", ex.Message);
    }

    /// <summary>A synthetic row's key lives above the engine's 32-bit space (bit 63 set - see
    /// <see cref="VfsFile.Hash"/>), so probing its display path by CRC32 is an ordinary miss, never
    /// fragment XML handed to a caller expecting a real file's bytes.</summary>
    [Fact]
    public void A_fragment_path_probed_by_hash_is_a_miss_not_fragment_xml()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown);
        Assert.True(fragment.Hash > uint.MaxValue);
        Assert.Null(vfs.ReadByPath(fragment.Path));
    }

    /// <summary>A real archive entry whose hash happens to equal a fragment display path's CRC32 -
    /// the collision that used to let the fragment row silently shadow the real file (19 such files
    /// were hidden in a real install before synthetic rows got their own keyspace).</summary>
    [Fact]
    public void A_real_entry_colliding_with_a_fragment_paths_hash_stays_visible()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();

        string fragmentPath;
        using (var probe = GameVfs.Load(_install, names))
        {
            fragmentPath = probe.Files.Values.First(f => TestSupport.IsFcbFragment(f) && f.NameIsKnown).Path;
        }

        byte[] collidingContent = "not an fcb"u8.ToArray();
        uint collidingHash = NameHash.Compute(fragmentPath);
        WriteSingleEntryArchive(Path.Combine(_sandbox, "Data_Win32", "extra"), collidingHash, collidingContent);

        using var vfs = GameVfs.Load(_install, names);

        // Both rows coexist: the real entry at its engine hash, the fragment row at its own key.
        Assert.True(vfs.Files.TryGetValue(collidingHash, out VfsFile? real));
        Assert.False(real!.IsFragment);
        Assert.Equal(collidingContent, vfs.Read(collidingHash));
        Assert.Contains(vfs.Files.Values, f => f.IsFragment
            && f.Path.Equals(fragmentPath, StringComparison.OrdinalIgnoreCase));

        // And a loader probing the fragment's path gets the real entry's bytes - engine semantics.
        Assert.Equal(collidingContent, vfs.ReadByPath(fragmentPath));
    }

    private static void WriteSingleEntryArchive(string basePath, uint hash, byte[] content)
    {
        File.WriteAllBytes(basePath + ".dat", content);
        FatArchive.FromEntries([new FatEntry(hash, Offset: 0, content.Length, UncompressedSize: 0, CompressionScheme.None)])
            .Write(basePath + ".fat");
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
