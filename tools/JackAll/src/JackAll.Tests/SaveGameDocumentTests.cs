using JackAll.Core.Format.Fcb;
using JackAll.Tools.Sav;

namespace JackAll.Tests;

/// <summary>
/// Unlike the other Format tests, this one runs against a hand-built synthetic file rather than a
/// real shipped sample: a real .sav is a player's personal save data (tens of thousands of persisted
/// entities plus a screenshot of their playthrough), not something to check into the repo as a test
/// fixture. The byte layout built here matches reverse/dunia/savegame_format.md exactly, which was
/// itself derived by decompiling the real reader/writer and cross-checking every offset against a
/// real save file byte-for-byte — see that doc for the evidence trail.
/// </summary>
public class SaveGameDocumentTests
{
    private static byte[] BuildMinimalSaveGame(
        string world = "world1", string player = "Paul_Ferenc",
        int thumbWidth = 2, int thumbHeight = 2,
        string[]? dlcIds = null, uint persistedObjectCount = 42)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        TestSupport.WriteSaveWrapper(writer, world, player, thumbWidth, thumbHeight, dlcIds ?? ["dlc1"]);

        writer.Write(0x4643626Eu); // "FCbn"
        writer.Write((ushort)2);   // version
        writer.Write((ushort)0);   // flags
        writer.Write(persistedObjectCount);
        writer.Write((uint)0);     // totalValueCount — not read by SaveGameDocument

        return stream.ToArray();
    }

    [Fact]
    public void Reads_world_and_player_name()
    {
        SaveGameInfo info = SaveGameDocument.Read(new MemoryStream(BuildMinimalSaveGame()), "test.sav");

        Assert.Equal("world1", info.WorldName);
        Assert.Equal("Paul_Ferenc", info.PlayerName);
    }

    [Fact]
    public void Reads_thumbnail_dimensions_and_pixel_data_size()
    {
        SaveGameInfo info = SaveGameDocument.Read(
            new MemoryStream(BuildMinimalSaveGame(thumbWidth: 4, thumbHeight: 3)), "test.sav");

        Assert.Equal(4, info.ThumbnailWidth);
        Assert.Equal(3, info.ThumbnailHeight);
        Assert.Equal(4 * 3 * 4, info.ThumbnailPixels.Length);
    }

    [Fact]
    public void Reads_active_dlc_ids()
    {
        SaveGameInfo info = SaveGameDocument.Read(
            new MemoryStream(BuildMinimalSaveGame(dlcIds: ["dlc1", "dlc_jungle"])), "test.sav");

        Assert.Equal(["dlc1", "dlc_jungle"], info.ActiveDlcIds);
    }

    [Fact]
    public void Reads_persisted_object_count_from_the_embedded_fcb_header()
    {
        SaveGameInfo info = SaveGameDocument.Read(
            new MemoryStream(BuildMinimalSaveGame(persistedObjectCount: 73_200)), "test.sav");

        Assert.Equal(73_200u, info.PersistedObjectCount);
    }

    [Fact]
    public void Fcb_blob_offset_points_at_the_real_FCbn_magic()
    {
        byte[] file = BuildMinimalSaveGame();
        SaveGameInfo info = SaveGameDocument.Read(new MemoryStream(file), "test.sav");

        Assert.Equal(0x4643626Eu, BitConverter.ToUInt32(file, (int)info.FcbBlobOffset));
    }

    [Fact]
    public void Rejects_a_file_with_no_fcb_blob_after_the_dlc_list()
    {
        byte[] file = BuildMinimalSaveGame();
        // Corrupt the FCbn magic that immediately follows the DLC list's trailing reserved field.
        var info = SaveGameDocument.Read(new MemoryStream(file), "probe");
        file[info.FcbBlobOffset] = 0;

        Assert.Throws<InvalidDataException>(() => SaveGameDocument.Read(new MemoryStream(file), "test.sav"));
    }

    [Fact]
    public void Rejects_nonzero_screenshot_metadata_rather_than_misparse_past_it()
    {
        byte[] file = BuildMinimalSaveGame();

        // The metadata count is the 4 bytes immediately after the thumbnail pixel data: header(20) +
        // world(4+6) + player(4+11) + trailer(12) + screenshotHeader(16) + pixels(2*2*4=16).
        int metadataCountOffset = 20 + (4 + 6) + (4 + 11) + 12 + 16 + 16;
        BitConverter.GetBytes((uint)1).CopyTo(file, metadataCountOffset);

        Assert.Throws<NotSupportedException>(() => SaveGameDocument.Read(new MemoryStream(file), "test.sav"));
    }

    /// <summary>Builds a tiny, real, decodable <c>FcbObject</c> tree - one root with one String value
    /// and one child object with one UInt32 value - matching the actual save PersistenceDB tree's shape
    /// closely enough (see reverse/dunia/savegame_format.md) to exercise <see cref="FcbDocument.Serialize"/>
    /// for real, not just an empty root.</summary>
    private static FcbObject BuildSampleTree(string rootValueText = "Addi Mbantuwe", uint childValue = 7)
    {
        var child = new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii("HierarchyRecord") };
        child.Values[FcbClassDefinitions.Crc32Ascii("MemoryUsage")] = BitConverter.GetBytes(childValue);

        var root = new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii("Entities") };
        root.Values[FcbClassDefinitions.Crc32Ascii("Name")] =
            [.. System.Text.Encoding.ASCII.GetBytes(rootValueText), 0];
        root.Children.Add(child);
        return root;
    }

    [Fact]
    public void WriteFcbRoot_replaces_the_blob_and_leaves_the_wrapper_untouched()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, TestSupport.SaveGameWithTree(
                BuildSampleTree(), world: "world1", player: "Paul_Ferenc", dlcIds: ["dlc1", "dlc_jungle"]));

            SaveGameInfo before = SaveGameDocument.Read(path);
            FcbObject root = SaveGameDocument.ReadFcbRoot(before);

            // Edit the tree exactly the way the property grid does - mutate Values in place - then
            // write it back.
            uint nameHash = FcbClassDefinitions.Crc32Ascii("MemoryUsage");
            root.Children[0].Values[nameHash] = BitConverter.GetBytes(99u);
            SaveGameDocument.WriteFcbRoot(before, root, path);

            SaveGameInfo after = SaveGameDocument.Read(path);
            Assert.Equal(before.WorldName, after.WorldName);
            Assert.Equal(before.PlayerName, after.PlayerName);
            Assert.Equal(before.ActiveDlcIds, after.ActiveDlcIds);
            Assert.Equal(before.ThumbnailWidth, after.ThumbnailWidth);
            Assert.Equal(before.ThumbnailHeight, after.ThumbnailHeight);

            FcbObject reloaded = SaveGameDocument.ReadFcbRoot(after);
            Assert.Equal(99u, BitConverter.ToUInt32(reloaded.Children[0].Values[nameHash]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteFcbRoot_to_another_path_leaves_the_source_save_byte_for_byte_intact()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        string copyPath = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, TestSupport.SaveGameWithTree(BuildSampleTree()));
            byte[] original = File.ReadAllBytes(path);

            SaveGameInfo before = SaveGameDocument.Read(path);
            FcbObject root = SaveGameDocument.ReadFcbRoot(before);
            root.Children.Clear();
            SaveGameDocument.WriteFcbRoot(before, root, copyPath);

            Assert.Equal(original, File.ReadAllBytes(path));

            SaveGameInfo copy = SaveGameDocument.Read(copyPath);
            Assert.Equal(before.WorldName, copy.WorldName);
            Assert.Equal(before.PlayerName, copy.PlayerName);
            Assert.Empty(SaveGameDocument.ReadFcbRoot(copy).Children);
        }
        finally
        {
            File.Delete(path);
            File.Delete(copyPath);
        }
    }

    [Fact]
    public void WriteFcbRoot_tolerates_the_edited_tree_growing_larger_than_the_original_blob()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, TestSupport.SaveGameWithTree(BuildSampleTree(rootValueText: "short")));

            SaveGameInfo before = SaveGameDocument.Read(path);
            FcbObject root = SaveGameDocument.ReadFcbRoot(before);

            uint nameHash = FcbClassDefinitions.Crc32Ascii("Name");
            root.Values[nameHash] = [.. System.Text.Encoding.ASCII.GetBytes("a much, much longer replacement name"), 0];
            SaveGameDocument.WriteFcbRoot(before, root, path);

            SaveGameInfo after = SaveGameDocument.Read(path);
            FcbObject reloaded = SaveGameDocument.ReadFcbRoot(after);
            Assert.Equal(
                "a much, much longer replacement name",
                System.Text.Encoding.ASCII.GetString(reloaded.Values[nameHash], 0, reloaded.Values[nameHash].Length - 1));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
