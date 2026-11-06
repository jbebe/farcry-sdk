using JackAll.Core.Format;

namespace JackAll.Tools.SoundLabels;

/// <summary>What one `.spk` file is: a generic <see cref="Category"/>, a free-text <see cref="Detail"/>,
/// how the label was found (<see cref="Source"/>) and the files that name the sound.</summary>
public sealed record SoundLabel(string Path, string Category, string Detail, string Source, string Evidence)
{
    /// <summary>The category and detail as one line, for a grid cell or a search.</summary>
    public string Text { get; } = Detail.Length == 0 ? Category : $"{Category} · {Detail}";
}

/// <summary>
/// The checked-in label list (<c>assets/fc2.soundlabels.tsv</c>), looked up by file hash. A missing or
/// unreadable list loads empty.
/// </summary>
public sealed class SoundLabelTable
{
    private const string Header = "# path\tcategory\tdetail\tsource\tevidence";

    private readonly Dictionary<uint, SoundLabel> _byHash = [];

    /// <summary>A later label for the same path replaces an earlier one.</summary>
    public SoundLabelTable(IEnumerable<SoundLabel> labels)
    {
        foreach (SoundLabel label in labels)
        {
            _byHash[NameHash.Compute(label.Path)] = label;
        }
    }

    public IEnumerable<SoundLabel> Labels => _byHash.Values;

    public SoundLabel? Find(uint hash) => _byHash.GetValueOrDefault(hash);

    public static SoundLabelTable Load(string path)
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

    public static SoundLabelTable Parse(IEnumerable<string> lines) => new(
        from line in lines
        where line.Length > 0 && line[0] != '#'
        let f = line.Split('\t')
        where f.Length == 5
        select new SoundLabel(f[0], f[1], f[2], f[3], f[4]));

    public IEnumerable<string> Write() => Labels
        .OrderBy(l => l.Path, StringComparer.Ordinal)
        .Select(l => string.Join('\t', l.Path, l.Category, Clean(l.Detail), l.Source, Clean(l.Evidence)))
        .Prepend(Header);

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
