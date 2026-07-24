using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Tests;

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
        string xml = FcbXml.ToXml(replacement, FcbClassDefinitions.Empty).IndexXml;
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

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        byte[] originalFragmentContent = vfs.Read(fragment.Hash);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] replacement = BuildReplacementFragmentXml();
        workspace.Stage(fragment.Hash, fragment.Path, "xml", replacement);
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
        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        VfsFile containerBefore = vfs.Files[fragment.ContainerHash!.Value];
        VfsFile? sibling = vfs.Files.Values.FirstOrDefault(
            f => f.IsFragment && f.ContainerHash == fragment.ContainerHash && f.Hash != fragment.Hash);
        byte[] originalFragmentContent = vfs.Read(fragment.Hash);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] replacement = BuildReplacementFragmentXml();
        workspace.Stage(fragment.Hash, fragment.Path, "xml", replacement);
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
        Assert.NotEqual(vfs.ReadOriginal(containerBefore.Hash), assembledContainer);

        // An un-overridden sibling fragment is completely unaffected.
        if (sibling is not null)
        {
            VfsFile stillPlain = vfs.Files[sibling.Hash];
            Assert.False(stillPlain.IsOverriding);
            Assert.False(stillPlain.IsModded);
            Assert.Equal(SourceKind.Archive, stillPlain.SourceKind);
        }

        // Reverting removes the override from both the fragment row and the container's attribution.
        Assert.True(workspace.Unstage(fragment.Hash));
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

        VfsFile existingFragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        VfsFile container = vfs.Files[existingFragment.ContainerHash!.Value];
        int fragmentRowCountBefore = vfs.Files.Values.Count(f => f.ContainerHash == container.Hash);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] addition = BuildReplacementFragmentXml();
        string newFragmentPath = $"{container.Path}\\99999_does_not_exist_in_vanilla.xml";
        uint newFragmentHash = NameHash.Compute(newFragmentPath);
        workspace.Stage(newFragmentHash, newFragmentPath, "xml", addition);
        vfs.Rebuild([workspace]);

        Assert.Equal(fragmentRowCountBefore + 1, vfs.Files.Values.Count(f => f.ContainerHash == container.Hash));

        VfsFile added = vfs.Files[newFragmentHash];
        Assert.True(added.IsFragment);
        Assert.True(added.IsModded);
        Assert.False(added.IsOverriding); // not overriding an existing child - there wasn't one
        Assert.Equal("workspace", added.SourceName);
        Assert.Equal(addition, vfs.Read(newFragmentHash));

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

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        byte[] replacement = BuildReplacementFragmentXml();
        string hashAddressedPath = $"_hash\\{fragment.ContainerHash:x8}.fcb\\{fragment.FragmentId}";
        workspace.Stage(fragment.Hash, hashAddressedPath, "xml", replacement);
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
    {
        FcbObject root = FcbDocument.Deserialize(vfs.ReadOriginal(fragment.ContainerHash!.Value)!);
        int index = FcbXml.ListFragmentIds(root).ToList().IndexOf(fragment.FragmentId!);
        return root.Children[index];
    }

    private static byte[] RenderWithTopLevelValueSet(FcbObject vanilla, uint valueHash, byte[] value)
    {
        var edited = new FcbObject { TypeHash = vanilla.TypeHash };
        foreach ((uint hash, byte[] existing) in vanilla.Values)
        {
            edited.Values[hash] = existing;
        }
        edited.Values[valueHash] = value;
        foreach (FcbObject child in vanilla.Children)
        {
            edited.Children.Add(child);
        }
        string xml = FcbXml.ToXml(edited, FcbClassDefinitions.Empty).IndexXml;
        return System.Text.Encoding.UTF8.GetBytes(xml);
    }

    [Fact]
    public void Two_mods_editing_different_fields_of_the_same_fragment_both_survive_in_the_merged_read()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        FcbObject vanilla = VanillaFragmentObject(vfs, fragment);
        if (vanilla.Children.Count < 2) return; // fixture too small to prove non-overlapping edits safely

        var zipDir = Path.Combine(_sandbox, "zip_src");
        Directory.CreateDirectory(zipDir);
        string zipEntryPath = Path.Combine(zipDir, fragment.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(zipEntryPath)!);
        File.WriteAllBytes(zipEntryPath, TestSupport.RenderWithChildValueSet(vanilla, 0, 0xAAAA0001, [0x01, 0x00, 0x00, 0x00]));
        string zipPath = Path.Combine(_sandbox, "mod_a.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(zipDir, zipPath);
        var zipMod = new ZipModLayer(zipPath);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(fragment.Hash, fragment.Path, "xml", TestSupport.RenderWithChildValueSet(vanilla, 1, 0xAAAA0002, [0x02, 0x00, 0x00, 0x00]));

        vfs.Rebuild([zipMod, workspace]);

        VfsFile mergedFragment = vfs.Files[fragment.Hash];
        Assert.True(mergedFragment.IsModded);
        Assert.Equal("multiple mods", mergedFragment.SourceName);

        FcbObject merged = FcbXml.FromXml(System.Text.Encoding.UTF8.GetString(vfs.Read(fragment.Hash)));
        Assert.Equal([0x01, 0x00, 0x00, 0x00], merged.Children[0].Values[0xAAAA0001]);
        Assert.Equal([0x02, 0x00, 0x00, 0x00], merged.Children[1].Values[0xAAAA0002]);

        // The container composes both edits too, not just the standalone fragment row.
        FcbObject container = FcbDocument.Deserialize(vfs.Read(fragment.ContainerHash!.Value));
        int index = FcbXml.ListFragmentIds(FcbDocument.Deserialize(vfs.ReadOriginal(fragment.ContainerHash!.Value)!))
            .ToList().IndexOf(fragment.FragmentId!);
        Assert.Equal([0x01, 0x00, 0x00, 0x00], container.Children[index].Children[0].Values[0xAAAA0001]);
        Assert.Equal([0x02, 0x00, 0x00, 0x00], container.Children[index].Children[1].Values[0xAAAA0002]);
    }

    [Fact]
    public void Two_mods_editing_the_same_field_differently_throws_a_conflict_naming_the_mod()
    {
        if (_install is null) return;

        NameDatabase names = TestSupport.LoadNames();
        using var vfs = GameVfs.Load(_install, names);

        VfsFile fragment = vfs.Files.Values.First(f => f.IsFragment && f.NameIsKnown);
        FcbObject vanilla = VanillaFragmentObject(vfs, fragment);
        if (vanilla.Values.Count == 0) return; // fixture has nothing existing to collide on

        uint existingHash = vanilla.Values.Keys.First();

        var zipDir = Path.Combine(_sandbox, "zip_src_conflict");
        Directory.CreateDirectory(zipDir);
        string zipEntryPath = Path.Combine(zipDir, fragment.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(zipEntryPath)!);
        // Same existing field, different content - a genuine collision, not two independent adds.
        File.WriteAllBytes(zipEntryPath, RenderWithTopLevelValueSet(vanilla, existingHash, [0x01, 0x00, 0x00, 0x00]));
        string zipPath = Path.Combine(_sandbox, "mod_conflict.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(zipDir, zipPath);
        var zipMod = new ZipModLayer(zipPath);

        var workspace = new FolderModLayer(_workspaceDir, "workspace");
        workspace.Stage(fragment.Hash, fragment.Path, "xml", RenderWithTopLevelValueSet(vanilla, existingHash, [0xFF, 0x00, 0x00, 0x00]));

        vfs.Rebuild([zipMod, workspace]);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => vfs.Read(fragment.Hash));
        Assert.Contains("workspace", ex.Message);
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
