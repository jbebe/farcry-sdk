using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace JackAll.App.Domino;

/// <summary>
/// Measures rendered text width, so a node can be sized to the port names it actually has rather than
/// to a guess. An average-character-width estimate is not good enough here: pin names in this corpus
/// range from `In` to `_4a__Wager_finished__Buddy_healthy`, and getting it wrong pushes the connector
/// outside the node body, which drags the wire's anchor along with it.
///
/// Results are cached because a big graph measures thousands of strings drawn from a much smaller set
/// of distinct names, and <see cref="FormattedText"/> is not cheap.
/// </summary>
internal static class TextMetrics
{
    private static readonly Typeface Regular = new("Segoe UI");
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Dictionary<(string Text, double Size, bool Bold), double> Cache = [];

    public static double Width(string? text, double fontSize, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var key = (text, fontSize, bold);
        if (Cache.TryGetValue(key, out double cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            bold ? Bold : Regular,
            fontSize,
            Brushes.Black,
            pixelsPerDip: 1.0);

        Cache[key] = formatted.Width;
        return formatted.Width;
    }
}
