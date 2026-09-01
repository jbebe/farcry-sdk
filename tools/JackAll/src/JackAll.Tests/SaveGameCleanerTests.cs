using JackAll.Core.Format.Fcb;
using JackAll.Tools.Sav;

namespace JackAll.Tests;

/// <summary>Built against the tree shape a save has: a PersistenceDB node per world carrying four
/// record containers, beside the campaign-progress sections under the save root.</summary>
public class SaveGameCleanerTests
{
    private static FcbObject Node(string name, params FcbObject[] children)
    {
        var node = new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii(name) };
        node.Children.AddRange(children);
        return node;
    }

    /// <summary>One PersistenceDB, beside a MissionManagement sibling standing in for everything a
    /// purge must not touch.</summary>
    private static FcbObject BuildSaveTree() => Node("CampaignSave",
        Node("MissionManagement", Node("ActiveMissions")),
        Node("PersistenceDB",
            Node("HierarchiesQueue", Node("Entry")),
            Node("Hierarchy", Node("HierarchyRecord")),
            Node("Entities", Node("Record", Node("State")), Node("Record", Node("State"), Node("Description"))),
            Node("OmniEntities")));

    private static FcbObject Child(FcbObject parent, string name)
        => parent.Children.Single(c => c.TypeHash == FcbClassDefinitions.Crc32Ascii(name));

    [Fact]
    public void Empties_every_record_container_but_keeps_the_containers()
    {
        FcbObject root = BuildSaveTree();

        SaveGameCleaner.PurgePersistedEntities(root);

        FcbObject database = Child(root, "PersistenceDB");
        Assert.Equal(4, database.Children.Count);
        Assert.All(database.Children, container => Assert.Empty(container.Children));
    }

    [Fact]
    public void Counts_records_and_every_object_below_them()
    {
        PurgeReport report = SaveGameCleaner.PurgePersistedEntities(BuildSaveTree());

        Assert.Equal(1, report.DatabasesEmptied);
        Assert.Equal(4, report.RecordsRemoved);
        // 4 records, plus the two States and one Description hanging off the Entities records.
        Assert.Equal(7, report.ObjectsRemoved);
    }

    [Fact]
    public void Leaves_campaign_progress_beside_the_database_untouched()
    {
        FcbObject root = BuildSaveTree();

        SaveGameCleaner.PurgePersistedEntities(root);

        Assert.Single(Child(root, "MissionManagement").Children);
    }

    [Fact]
    public void Empties_every_world_s_database()
    {
        FcbObject root = Node("CampaignSave",
            Node("PersistenceDB", Node("Entities", Node("Record"))),
            Node("PersistenceDB", Node("Entities", Node("Record"), Node("Record"))));

        PurgeReport report = SaveGameCleaner.PurgePersistedEntities(root);

        Assert.Equal(2, report.DatabasesEmptied);
        Assert.Equal(3, report.RecordsRemoved);
    }

    [Fact]
    public void Reports_nothing_removed_for_a_save_that_has_persisted_nothing()
    {
        FcbObject root = Node("CampaignSave", Node("PersistenceDB", Node("Entities"), Node("OmniEntities")));

        PurgeReport report = SaveGameCleaner.PurgePersistedEntities(root);

        Assert.Equal(new PurgeReport(DatabasesEmptied: 1, RecordsRemoved: 0, ObjectsRemoved: 0), report);
    }

    [Fact]
    public void Ignores_a_child_of_the_database_that_is_not_a_record_container()
    {
        FcbObject root = Node("CampaignSave", Node("PersistenceDB", Node("SomethingElse", Node("Keep"))));

        SaveGameCleaner.PurgePersistedEntities(root);

        Assert.Single(Child(Child(root, "PersistenceDB"), "SomethingElse").Children);
    }

    [Fact]
    public void PurgeToNewSave_refuses_to_write_over_the_save_it_is_reading()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, TestSupport.SaveGameWithTree(BuildSaveTree()));
            SaveGameInfo info = SaveGameDocument.Read(path);

            Assert.Throws<IOException>(() => SaveGameCleaner.PurgeToNewSave(info, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PurgeToNewSave_refuses_to_overwrite_an_existing_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        string occupied = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, TestSupport.SaveGameWithTree(BuildSaveTree()));
            File.WriteAllText(occupied, "someone else's save");
            SaveGameInfo info = SaveGameDocument.Read(path);

            Assert.Throws<IOException>(() => SaveGameCleaner.PurgeToNewSave(info, occupied));
            Assert.Equal("someone else's save", File.ReadAllText(occupied));
        }
        finally
        {
            File.Delete(path);
            File.Delete(occupied);
        }
    }

    [Fact]
    public void PurgeToNewSave_writes_a_purged_copy_and_leaves_the_source_alone()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-test-{Guid.NewGuid():N}.sav");
        string? destPath = null;
        try
        {
            File.WriteAllBytes(path, TestSupport.SaveGameWithTree(BuildSaveTree()));
            byte[] original = File.ReadAllBytes(path);
            SaveGameInfo info = SaveGameDocument.Read(path);

            (destPath, PurgeReport report) = SaveGameCleaner.PurgeToNewSave(info);

            Assert.Equal(4, report.RecordsRemoved);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(destPath));

            FcbObject purged = SaveGameDocument.ReadFcbRoot(SaveGameDocument.Read(destPath));
            Assert.All(Child(purged, "PersistenceDB").Children, c => Assert.Empty(c.Children));
            Assert.Single(Child(purged, "MissionManagement").Children);
        }
        finally
        {
            File.Delete(path);
            if (destPath is not null) File.Delete(destPath);
        }
    }
}
