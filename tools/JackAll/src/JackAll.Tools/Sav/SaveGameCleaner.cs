using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Sav;

/// <summary>What a purge removed, or would remove.</summary>
/// <param name="DatabasesEmptied">PersistenceDB nodes found; zero means the tree wasn't the expected shape.</param>
/// <param name="RecordsRemoved">Entity and hierarchy records dropped.</param>
/// <param name="ObjectsRemoved">Every object inside those records, counted recursively.</param>
public sealed record PurgeReport(int DatabasesEmptied, int RecordsRemoved, int ObjectsRemoved);

/// <summary>
/// Drops a save's record of what happened to the world, so every entity respawns from the game's
/// current <c>entitylibrary.fcb</c> instead of from whatever the save froze about it — how a mod that
/// changes an entity takes effect on a save made before it was installed. Costs world state only:
/// mission progress, buddies, tapes and diamonds sit beside <c>PersistenceDB</c> under the save root
/// rather than inside it.
/// </summary>
public static class SaveGameCleaner
{
    private static readonly uint PersistenceDbTag = FcbClassDefinitions.Crc32Ascii("PersistenceDB");

    /// <summary>The record containers <c>CPersistenceDB::SaveDB</c> creates on every PersistenceDB node.</summary>
    private static readonly uint[] RecordContainerTags =
    [
        FcbClassDefinitions.Crc32Ascii("HierarchiesQueue"),
        FcbClassDefinitions.Crc32Ascii("Hierarchy"),
        FcbClassDefinitions.Crc32Ascii("Entities"),
        FcbClassDefinitions.Crc32Ascii("OmniEntities"),
    ];

    /// <summary>
    /// Purges <paramref name="info"/>'s persisted entities into a new save, leaving the source file
    /// untouched. <paramref name="destPath"/> defaults to a fresh game-style name in the source's own
    /// folder; an explicit path is refused if it is the source or already exists.
    /// </summary>
    public static (string DestPath, PurgeReport Report) PurgeToNewSave(SaveGameInfo info, string? destPath = null)
    {
        destPath ??= SaveGameLocator.GenerateSaveFilePath(
            Path.GetDirectoryName(Path.GetFullPath(info.FilePath)) ?? SaveGameLocator.SavedGamesFolder);

        if (Path.GetFullPath(destPath).Equals(Path.GetFullPath(info.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"'{destPath}' is the save being read; a purge always writes a new file.");
        }
        if (File.Exists(destPath))
        {
            throw new IOException($"'{destPath}' already exists; refusing to overwrite it.");
        }

        FcbObject root = SaveGameDocument.ReadFcbRoot(info);
        PurgeReport report = PurgePersistedEntities(root);
        SaveGameDocument.WriteFcbRoot(info, root, destPath);
        return (destPath, report);
    }

    /// <summary>
    /// Empties every PersistenceDB in <paramref name="root"/>. The four record containers are kept and
    /// left childless — the shape a game that has persisted nothing yet writes — rather than deleted.
    /// </summary>
    public static PurgeReport PurgePersistedEntities(FcbObject root)
    {
        int databases = 0, records = 0, objects = 0;

        foreach (FcbObject database in FindPersistenceDatabases(root))
        {
            databases++;
            foreach (FcbObject container in database.Children)
            {
                if (!RecordContainerTags.Contains(container.TypeHash))
                {
                    continue;
                }

                records += container.Children.Count;
                objects += CountObjects(container) - 1;
                container.Children.Clear();
            }
        }

        return new PurgeReport(databases, records, objects);
    }

    private static IEnumerable<FcbObject> FindPersistenceDatabases(FcbObject root)
    {
        var pending = new Stack<FcbObject>([root]);
        while (pending.Count > 0)
        {
            FcbObject node = pending.Pop();
            if (node.TypeHash == PersistenceDbTag)
            {
                yield return node;
                continue;
            }

            foreach (FcbObject child in node.Children)
            {
                pending.Push(child);
            }
        }
    }

    private static int CountObjects(FcbObject node)
    {
        int count = 1;
        for (int i = 0; i < node.Children.Count; i++)
        {
            count += CountObjects(node.Children[i]);
        }
        return count;
    }
}
