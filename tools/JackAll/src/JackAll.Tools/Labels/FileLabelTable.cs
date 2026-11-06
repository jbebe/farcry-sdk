using JackAll.Core.Format;

namespace JackAll.Tools.Labels;

/// <summary>What one game file is: a generic <see cref="Category"/>, a free-text <see cref="Detail"/>,
/// how the label was found (<see cref="Source"/>) and what it was found from.</summary>
public sealed record FileLabel(string Path, string Category, string Detail, string Source, string Evidence)
{
    /// <summary>A label set by hand, which a labeler run keeps over the one it finds.</summary>
    public const string FromHand = "hand";

    /// <summary>The category and detail as one line, for a grid cell or a search.</summary>
    public string Text { get; } = Detail.Length == 0 ? Category : $"{Category} · {Detail}";
}

/// <summary>
/// A checked-in label list (<c>assets/fc2.soundlabels.tsv</c>, <c>assets/fc2.shaderlabels.tsv</c>),
/// looked up by file hash. A missing or unreadable list loads empty.
/// </summary>
public sealed class FileLabelTable
{
    private const string Header = "# path\tcategory\tdetail\tsource\tevidence";

    private readonly Dictionary<uint, FileLabel> _byHash = [];

    /// <summary>A later label for the same path replaces an earlier one.</summary>
    public FileLabelTable(IEnumerable<FileLabel> labels)
    {
        foreach (FileLabel label in labels)
        {
            _byHash[NameHash.Compute(label.Path)] = label;
        }
    }

    public IEnumerable<FileLabel> Labels => _byHash.Values;

    public FileLabel? Find(uint hash) => _byHash.GetValueOrDefault(hash);

    public static FileLabelTable Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadLines(path)) : new([]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([]);
        }
    }

    public static FileLabelTable Parse(IEnumerable<string> lines) => new(
        from line in lines
        where line.Length > 0 && line[0] != '#'
        let f = line.Split('\t')
        where f.Length == 5
        select new FileLabel(f[0], f[1], f[2], f[3], f[4]));

    /// <summary>The found labels with every hand label kept: those already in <paramref name="output"/>
    /// and those in <paramref name="handFile"/>, which win over found ones.</summary>
    public static FileLabelTable MergeHand(IEnumerable<FileLabel> found, string output, string? handFile) => new(
    [
        .. found,
        .. Load(output).Labels.Where(l => l.Source == FileLabel.FromHand),
        .. handFile is null ? [] : Load(handFile).Labels.Select(l => l with { Source = FileLabel.FromHand }),
    ]);

    public IEnumerable<string> Write() => Labels
        .OrderBy(l => l.Path, StringComparer.Ordinal)
        .Select(l => string.Join('\t', l.Path, l.Category, Clean(l.Detail), l.Source, Clean(l.Evidence)))
        .Prepend(Header);

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>Text helpers the labelers share.</summary>
internal static class LabelFormat
{
    /// <summary>How many of <paramref name="count"/> items went unshown, as <c>prefix + n + suffix</c>; empty when none did.</summary>
    public static string More(int count, int shown, string prefix, string suffix = "")
        => count > shown ? $"{prefix}{count - shown}{suffix}" : "";

    /// <summary>The non-empty parts, joined.</summary>
    public static string Join(string separator, params string[] parts)
        => string.Join(separator, parts.Where(part => part.Length > 0));
}
