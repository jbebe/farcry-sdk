using JackAll.Core.Format;

namespace JackAll.Tools.Labels;

/// <summary>
/// The asset tag tables of <c>tools/asset-tags</c> (models, textures, sound banks, shader objects): what
/// each file is and where it belongs, looked up by file hash. A missing or unreadable table loads empty.
/// </summary>
public sealed class AssetTagTable
{
    // Columns of whole words: the kind, the tags, and the traits and tags the build derives.
    private static readonly string[] WordColumns = ["kind", "tags", "inherited", "traits"];

    /// <summary>A row's whole words, and the same joined for part-of-word search; rows share one per set.</summary>
    private sealed record WordSet(string[] Words, string Text);

    private sealed record Entry(string Label, string Tags, WordSet Words, HashSet<string> Vocabulary, string What);

    private readonly Dictionary<uint, Entry> _byHash = [];

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

    /// <summary>Adds one table: a <c># path	kind	...</c> header, then a row per file. Its words are
    /// its own vocabulary.</summary>
    public void Add(IEnumerable<string> lines)
    {
        var vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wordSets = new Dictionary<string, WordSet>(StringComparer.OrdinalIgnoreCase);
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

            string[] words = [.. WordColumns.SelectMany(c => Column(c).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
            vocabulary.UnionWith(words);
            string key = string.Join(' ', words);
            if (!wordSets.TryGetValue(key, out WordSet? shared))
            {
                wordSets[key] = shared = new WordSet(words, key);
            }
            string detail = Column("detail");
            string what = Column("what");
            _byHash[NameHash.Compute(fields[0])] = new Entry(LabelFormat.Join(" · ", Column("kind"), detail.Length > 0 ? detail : what),
                Column("tags"), shared, vocabulary, detail.Length > 0 ? what : "");
        }
    }

    /// <summary>What the file is: its kind, then its labeler's detail or else its description.</summary>
    public string? Find(uint hash) => _byHash.GetValueOrDefault(hash)?.Label;

    /// <summary>The file's tags, space-separated; null for a file the tables lack.</summary>
    public string? TagsOf(uint hash) => _byHash.GetValueOrDefault(hash)?.Tags;

    /// <summary>
    /// Whether a tagged file matches a word. A word that is a kind, tag or trait in the file's table
    /// matches only the file's own, so <c>war</c> does not find warehouses; any other word matches part
    /// of its label, words or description.
    /// </summary>
    public bool Matches(uint hash, string word)
    {
        if (_byHash.GetValueOrDefault(hash) is not { } entry)
        {
            return false;
        }
        return entry.Vocabulary.Contains(word)
            ? entry.Words.Words.Contains(word, StringComparer.OrdinalIgnoreCase)
            : entry.Label.Contains(word, StringComparison.OrdinalIgnoreCase)
                || entry.Words.Text.Contains(word, StringComparison.OrdinalIgnoreCase)
                || entry.What.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}
