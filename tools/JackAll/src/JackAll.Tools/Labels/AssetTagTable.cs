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

    private sealed record Entry(string Label, string Tags, string[] Words, HashSet<string> Vocabulary, string What);

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

    /// <summary>Adds one table: a <c>#</c> header naming its columns, then a row per file. Its words are
    /// its own vocabulary.</summary>
    public void Add(IEnumerable<string> lines)
    {
        var vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Strings and word lists that many rows repeat, held once.
        var strings = new Dictionary<string, string>();
        var wordLists = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        string Shared(string text) => strings.TryGetValue(text, out string? held) ? held : strings[text] = text;

        int[]? wordColumns = null;
        int kind = -1, what = -1, tags = -1, detail = -1;
        var words = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string[] fields = line.Split('\t');
            if (wordColumns is null)
            {
                fields[0] = fields[0].TrimStart('#', ' ');
                wordColumns = [.. WordColumns.Select(c => Array.IndexOf(fields, c))];
                (kind, what, tags, detail) = (Array.IndexOf(fields, "kind"), Array.IndexOf(fields, "what"),
                    Array.IndexOf(fields, "tags"), Array.IndexOf(fields, "detail"));
                continue;
            }

            words.Clear();
            foreach (int column in wordColumns)
            {
                words.UnionWith(At(fields, column).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            vocabulary.UnionWith(words);
            string key = string.Join(' ', words);
            if (!wordLists.TryGetValue(key, out string[]? shared))
            {
                wordLists[key] = shared = [.. words];
            }

            string description = Shared(At(fields, what));
            string labelDetail = At(fields, detail);
            string label = LabelFormat.Join(" · ", At(fields, kind), labelDetail.Length > 0 ? labelDetail : description);
            _byHash[NameHash.Compute(fields[0])] = new Entry(Shared(label), Shared(At(fields, tags)), shared, vocabulary, description);
        }
    }

    private static string At(string[] fields, int column) => column >= 0 && column < fields.Length ? fields[column] : "";

    /// <summary>What the file is: its kind, then its labeler's detail or else its description.</summary>
    public string? Find(uint hash) => _byHash.GetValueOrDefault(hash)?.Label;

    /// <summary>The file's tags, space-separated; null for a file the tables lack.</summary>
    public string? TagsOf(uint hash) => _byHash.GetValueOrDefault(hash)?.Tags;

    /// <summary>Whether a tagged file matches a word: a word of its table's vocabulary only as one of the
    /// file's own words, any other word as part of its label, words or description.</summary>
    public bool Matches(uint hash, string word)
    {
        if (_byHash.GetValueOrDefault(hash) is not { } entry)
        {
            return false;
        }
        return entry.Vocabulary.Contains(word)
            ? entry.Words.Contains(word, StringComparer.OrdinalIgnoreCase)
            : entry.Label.Contains(word, StringComparison.OrdinalIgnoreCase)
                || entry.Words.Any(w => w.Contains(word, StringComparison.OrdinalIgnoreCase))
                || entry.What.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}
