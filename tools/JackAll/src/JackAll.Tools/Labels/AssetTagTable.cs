using JackAll.Core.Format;

namespace JackAll.Tools.Labels;

/// <summary>
/// The asset tag tables of <c>tools/asset-tags</c> (models, textures, sound banks): what each file is
/// and where it belongs, looked up by file hash. A missing or unreadable table loads empty.
/// </summary>
public sealed class AssetTagTable
{
    // Columns of whole words: the kind, the tags, and the traits and tags the build derives.
    private static readonly string[] WordColumns = ["kind", "tags", "inherited", "traits"];

    private sealed record Entry(FileLabel Label, string Tags, HashSet<string> Words, string Text);

    private readonly Dictionary<uint, Entry> _byHash = [];
    private readonly HashSet<string> _vocabulary = new(StringComparer.OrdinalIgnoreCase);

    public static AssetTagTable Load(IEnumerable<string> paths)
    {
        var table = new AssetTagTable();
        foreach (string path in paths)
        {
            try
            {
                table.Add(File.ReadLines(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return table;
    }

    /// <summary>Adds one table: a <c># path	kind	...</c> header, then a row per file.</summary>
    public void Add(IEnumerable<string> lines)
    {
        string[]? header = null;
        foreach (string line in lines)
        {
            if (header is null)
            {
                header = line.TrimStart('#', ' ').Split('\t');
                continue;
            }
            string[] fields = line.Split('\t');
            string Column(string name) => Array.IndexOf(header, name) is var i and >= 0 && i < fields.Length ? fields[i] : "";

            var words = new HashSet<string>(WordColumns.SelectMany(c => Column(c).Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                StringComparer.OrdinalIgnoreCase);
            var label = new FileLabel(fields[0], Column("kind"), Column("what"), FileLabel.FromHand, "");
            _byHash[NameHash.Compute(fields[0])] = new Entry(label, Column("tags"), words, $"{string.Join(' ', words)} {Column("what")}");
            _vocabulary.UnionWith(words);
        }
    }

    /// <summary>The file's kind and description as a label.</summary>
    public FileLabel? Find(uint hash) => _byHash.GetValueOrDefault(hash)?.Label;

    /// <summary>The file's tags, space-separated; null for a file the tables lack.</summary>
    public string? TagsOf(uint hash) => _byHash.GetValueOrDefault(hash)?.Tags;

    /// <summary>
    /// Whether a tagged file matches a word. A word that is a kind, tag or trait anywhere in the tables
    /// matches only the file's own, so <c>war</c> does not find warehouses; any other word matches part
    /// of its kind, tags, description or <paramref name="label"/>.
    /// </summary>
    public bool Matches(uint hash, string word, string? label = null) => _byHash.GetValueOrDefault(hash) is { } entry
        && (_vocabulary.Contains(word)
            ? entry.Words.Contains(word)
            : entry.Text.Contains(word, StringComparison.OrdinalIgnoreCase) || label?.Contains(word, StringComparison.OrdinalIgnoreCase) == true);
}
