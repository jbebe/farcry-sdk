using JackAll.Core.Text;

namespace JackAll.Tests;

/// <summary>
/// The compact "just the changes" view behind an overridden text file's preview.
/// </summary>
public class DiffTextBuilderTests
{
    /// <summary>
    /// Two identical texts produce no changed line at all. The preview keys off exactly this to say
    /// "identical to the base game" rather than promising changed lines and showing none - which a
    /// whole-file override makes a real case, since every part of its container reads as modded.
    /// </summary>
    [Fact]
    public void An_unchanged_file_yields_no_added_or_removed_line()
    {
        const string text = "one\ntwo\nthree\nfour\nfive";

        IReadOnlyList<DiffLine> diff = DiffTextBuilder.BuildTrimmedDiff(text, text);

        Assert.DoesNotContain(diff, l => l.Kind is DiffLineKind.Added or DiffLineKind.Removed);
    }

    /// <summary>The other half: a real edit does produce one, so the check above cannot pass by
    /// accident on a builder that returned nothing.</summary>
    [Fact]
    public void A_changed_line_yields_an_added_and_a_removed_line()
    {
        IReadOnlyList<DiffLine> diff = DiffTextBuilder.BuildTrimmedDiff(
            "one\ntwo\nthree", "one\nCHANGED\nthree");

        Assert.Contains(diff, l => l.Kind == DiffLineKind.Added && l.Text.Contains("CHANGED"));
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Removed && l.Text.Contains("two"));
    }
}
