using System.IO;
using JackAll.Core.Vfs;

namespace JackAll.App;

/// <summary>Builds and walks a <see cref="FolderNode"/> tree over a set of files.</summary>
internal static class FolderTree
{
    /// <summary>
    /// Refills <paramref name="index"/> with one sorted node per directory holding a file, keyed by
    /// path with the root at "", and returns the root. Folders open in the old index stay open, as do
    /// the ancestors of <paramref name="reveal"/>; every folder above a modded file is marked.
    /// <paramref name="onFile"/> sees each file's node.
    /// </summary>
    public static FolderNode Build(
        IEnumerable<VfsFile> files, Dictionary<string, FolderNode> index, string? reveal = null,
        Action<FolderNode, VfsFile>? onFile = null)
    {
        var expanded = new HashSet<string>(
            index.Values.Where(n => n.IsExpanded).Select(n => n.FullPath), StringComparer.OrdinalIgnoreCase);
        for (string? dir = Path.GetDirectoryName(reveal); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            expanded.Add(dir);
        }

        var root = new FolderNode("", "");
        index.Clear();
        index[""] = root;

        foreach (VfsFile file in files)
        {
            FolderNode node = Ensure(index, file.Directory, expanded);
            node.HasFiles = true;
            for (FolderNode? n = node; file.IsModded && n is not null; n = ParentOf(index, n))
            {
                n.ContainsMods = true;
            }
            onFile?.Invoke(node, file);
        }

        Sort(root);
        return root;
    }

    public static FolderNode? ParentOf(IReadOnlyDictionary<string, FolderNode> index, FolderNode node)
    {
        string? parent = Path.GetDirectoryName(node.FullPath);
        return string.IsNullOrEmpty(parent) ? null : index.GetValueOrDefault(parent);
    }

    /// <summary>The folders from a top-level root down to and including <paramref name="node"/>.</summary>
    public static IReadOnlyList<FolderNode> AncestorChain(IReadOnlyDictionary<string, FolderNode> index, FolderNode node)
    {
        var chain = new List<FolderNode>();
        for (FolderNode? current = node; current is not null; current = ParentOf(index, current))
        {
            chain.Insert(0, current);
        }
        return chain;
    }

    /// <summary>Drops every branch <paramref name="keep"/> rejects.</summary>
    public static void Prune(FolderNode node, Func<FolderNode, bool> keep)
    {
        var kept = node.Children.Where(keep).ToList();
        node.Children.Clear();
        foreach (FolderNode child in kept)
        {
            Prune(child, keep);
            node.Children.Add(child);
        }
    }

    private static FolderNode Ensure(Dictionary<string, FolderNode> index, string directory, IReadOnlySet<string> expanded)
    {
        if (index.TryGetValue(directory, out FolderNode? existing))
        {
            return existing;
        }

        FolderNode parent = Ensure(index, Path.GetDirectoryName(directory) ?? string.Empty, expanded);
        var node = new FolderNode(Path.GetFileName(directory), directory)
        {
            IsExpanded = expanded.Contains(directory),
        };
        parent.Children.Add(node);
        index[directory] = node;
        return node;
    }

    private static void Sort(FolderNode node)
    {
        var sorted = node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        node.Children.Clear();
        foreach (FolderNode child in sorted)
        {
            Sort(child);
            node.Children.Add(child);
        }
    }
}
