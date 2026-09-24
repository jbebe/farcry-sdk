using System.IO;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>Prefabs in the viewport: whatever moves, turns or deletes a prefab does the same to its
/// members, and grouping and ungrouping make and dissolve one.</summary>
public partial class MapTabView
{
    /// <summary>The members of every prefab <paramref name="entities"/> holds, each with its prefab.</summary>
    private Dictionary<WorldEntity, WorldEntity> MembersOf(IEnumerable<WorldEntity> entities)
    {
        var members = new Dictionary<WorldEntity, WorldEntity>();
        if (_edits is not { } edits)
        {
            return members;
        }

        foreach (WorldEntity prefab in entities)
        {
            foreach (WorldEntity member in edits.MembersOf(prefab))
            {
                members.TryAdd(member, prefab);
            }
        }
        return members;
    }

    /// <summary>The selection with every selected prefab's members added.</summary>
    private List<WorldEntity> SelectionWithMembers()
        => [.. _selection.Items.Concat(MembersOf(_selection.Items).Keys).Distinct()];

    /// <summary>Writes the selected prefab and its members to a bundle the library lists.</summary>
    private void SavePrefab_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_edits is not { } edits || _selection.Primary is not { } prefab || !EntityGroups.IsPrefab(edits.CurrentNode(prefab)))
        {
            StatusText.Text = "Select a prefab to save - Group makes one";
            return;
        }

        Directory.CreateDirectory(AppConfig.PrefabsDir);
        string name = FcbFragments.Sanitize(prefab.Name);
        string path = Path.Combine(AppConfig.PrefabsDir, name + PrefabBundle.Extension);
        File.WriteAllText(path, PrefabBundle.Write(edits.Copy(prefab)));
        _library.LoadPrefabs();
        StatusText.Text = $"Saved {prefab.Name} to the library's Prefabs";
    }

    private void PastePrefab(string bundle, System.Windows.Point point)
    {
        if (_edits is not { } edits || TerrainUnder(point) is not { } ground)
        {
            StatusText.Text = "Drop the prefab on the ground";
            return;
        }

        CopiedEntity copy;
        try
        {
            copy = PrefabBundle.Read(File.ReadAllText(bundle));
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException)
        {
            StatusText.Text = $"Could not read {Path.GetFileName(bundle)}: {ex.Message}";
            return;
        }

        List<WorldEntity?> drawLike = [LookalikeOf(copy.ArchetypeName),
            .. copy.Members.Select(m => LookalikeOf(FcbEntityFields.ReadString(m.Node, WorldHashes.TplCreatureType)))];
        if (Add(() => edits.Paste(copy, ground), drawLike) is [var pasted, ..])
        {
            StatusText.Text = $"Pasted {pasted.Name} with {copy.Members.Count} members";
        }
    }

    private void Group_Click(object sender, System.Windows.RoutedEventArgs e) => GroupSelected();

    private void Ungroup_Click(object sender, System.Windows.RoutedEventArgs e) => UngroupSelected();

    private void GroupSelected()
    {
        if (_edits is not { } edits || _selection.Count == 0)
        {
            return;
        }
        List<WorldEntity> members = [.. _selection.Items];
        if (Add(() => [edits.Group(members)], []) is [var prefab])
        {
            _selection.Replace([.. members, prefab]);
            StatusText.Text = $"Grouped {members.Count} entities under {prefab.Name}";
        }
    }

    /// <summary>Removes the selected prefab entities and leaves their members where they are.</summary>
    private void UngroupSelected()
    {
        if (_edits is not { } edits)
        {
            return;
        }
        List<WorldEntity> prefabs = [.. _selection.Items.Where(e => EntityGroups.IsPrefab(edits.CurrentNode(e)))];
        if (prefabs.Count == 0)
        {
            StatusText.Text = "Select a prefab to ungroup";
            return;
        }

        List<WorldEntity> members = [.. MembersOf(prefabs).Keys];
        _history.Push(PresenceStep.Deleted(edits, [.. prefabs.Select(edits.Delete)]));
        var gone = new HashSet<WorldEntity>(prefabs);
        _positionedEntities.RemoveAll(gone.Contains);
        _selection.Replace(members);
        EntitySetChanged();
        StatusText.Text = $"Ungrouped {members.Count} entities";
    }
}
