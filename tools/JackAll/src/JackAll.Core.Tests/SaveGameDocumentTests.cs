using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Sav;

namespace JackAll.Core.Tests;

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
    /// <summary>Sections 1-4 up to and including the field right before the embedded `.fcb` blob -
    /// shared by <see cref="BuildMinimalSaveGame"/> (which appends a header-only, no-object-tree blob;
    /// fine for every test here that only reads wrapper metadata) and the round-trip write tests below
    /// (which append a real, <see cref="FcbDocument.Serialize"/>-produced blob instead, since
    /// <see cref="SaveGameDocument.ReadFcbRoot"/>/<see cref="SaveGameDocument.WriteFcbRoot"/> need an
    /// actual object tree to read/replace).</summary>
    private static void WriteWrapper(
        BinaryWriter writer, string world, string player, int thumbWidth, int thumbHeight, string[] dlcIds)
    {
        // Section 1: CGameFileHeader base, 20 opaque bytes.
        writer.Write(new byte[20]);

        // Section 2: CCampaignGameFileHeader extension.
        WriteLengthPrefixedString(writer, world);
        WriteLengthPrefixedString(writer, player);
        writer.Write(new byte[12]); // 3 unconfirmed trailing u32s

        // Section 3: CScreenShot.
        writer.Write((uint)thumbWidth);
        writer.Write((uint)thumbHeight);
        writer.Write((uint)4); // channels
        writer.Write((uint)8); // bits per channel
        writer.Write(new byte[thumbWidth * thumbHeight * 4]);
        writer.Write((uint)0); // metadata entry count

        // Section 4: CCampaignGameFileData — DLC list, then the embedded .fcb blob.
        writer.Write((uint)dlcIds.Length);
        foreach (string dlc in dlcIds)
        {
            WriteLengthPrefixedString(writer, dlc);
        }
        writer.Write((uint)0); // unconfirmed extra field
    }

    private static byte[] BuildMinimalSaveGame(
        string world = "world1", string player = "Paul_Ferenc",
        int thumbWidth = 2, int thumbHeight = 2,
        string[]? dlcIds = null, uint persistedObjectCount = 42)
    {
        dlcIds ??= ["dlc1"];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteWrapper(writer, world, player, thumbWidth, thumbHeight, dlcIds);

        writer.Write(0x4643626Eu); // "FCbn"
        writer.Write((ushort)2);   // version
        writer.Write((ushort)0);   // flags
        writer.Write(persistedObjectCount);
        writer.Write((uint)0);     // totalValueCount — not read by SaveGameDocument

        return stream.ToArray();
    }

    /// <summary>Same wrapper shape as <see cref="BuildMinimalSaveGame"/>, but with a real, decodable
    /// `.fcb` blob (<paramref name="root"/> serialized via <see cref="FcbDocument.Serialize"/>) instead
    /// of a bare header - what <see cref="SaveGameDocument.ReadFcbRoot"/>/<see cref="SaveGameDocument.WriteFcbRoot"/>
    /// need to actually have an object tree to read or replace.</summary>
    private static byte[] BuildSaveGameWithFcbBlob(
        FcbObject root, string world = "world1", string player = "Paul_Ferenc",
        int thumbWidth = 2, int thumbHeight = 2, string[]? dlcIds = null)
    {
        dlcIds ??= ["dlc1"];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteWrapper(writer, world, player, thumbWidth, thumbHeight, dlcIds);
        writer.Write(FcbDocument.Serialize(root));

        return stream.ToArray();
    }

    private static void WriteLengthPrefixedString(BinaryWriter writer, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
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
            File.WriteAllBytes(path, BuildSaveGameWithFcbBlob(
                BuildSampleTree(), world: "world1", player: "Paul_Ferenc", dlcIds: ["dlc1", "dlc_jungle"]));

            SaveGameInfo before = SaveGameDocument.Read(path);
            FcbObject root = SaveGameDocument.ReadFcbRoot(before);

            // Edit the tree exactly the way the property grid does - mutate Values in place - then
            // write it back.
            uint nameHash = FcbClassDefinitions.Crc32Ascii("MemoryUsage");
            root.Children[0].Values[nameHash] = BitConverter.GetBytes(99u);
            SaveGameDocument.WriteFcbRoot(before, root);

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
    public void WriteFcbRoot_tolerates_the_edited_tree_growing_larger_than_the_original_blob()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, BuildSaveGameWithFcbBlob(BuildSampleTree(rootValueText: "short")));

            SaveGameInfo before = SaveGameDocument.Read(path);
            FcbObject root = SaveGameDocument.ReadFcbRoot(before);

            uint nameHash = FcbClassDefinitions.Crc32Ascii("Name");
            root.Values[nameHash] = [.. System.Text.Encoding.ASCII.GetBytes("a much, much longer replacement name"), 0];
            SaveGameDocument.WriteFcbRoot(before, root);

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
