using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace JackAll.App;

/// <summary>Paints a search box's <c>Tag</c> (a string, or a TextBlock for rich text) as its placeholder
/// background.</summary>
public sealed class PlaceholderBrushConverter : IValueConverter
{
    private static readonly Brush HintText = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        TextBlock hint = value as TextBlock ?? new TextBlock { Text = value as string };
        hint.Margin = new Thickness(4, 0, 0, 0);
        hint.Foreground = HintText;
        hint.FontStyle = FontStyles.Italic;
        return new VisualBrush(hint) { AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Center, Stretch = Stretch.None };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
