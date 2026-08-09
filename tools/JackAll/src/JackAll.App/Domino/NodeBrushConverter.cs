using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JackAll.App.Domino;

/// <summary>
/// Picks a node's fill or border by what kind of box it is, keeping the palette the previous canvas
/// established: blue for a persistent instance, amber for a pooled occurrence, green for a sub-graph,
/// grey for the graph's own boundary. A node whose type script couldn't be read gets a warning border,
/// because "this box has no ports" should look like missing information rather than a box with no pins.
///
/// Pass "border" as the converter parameter for the outline, anything else for the fill.
/// </summary>
public sealed class NodeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PersistentFill = Frozen(0xE8, 0xF0, 0xFE);
    private static readonly SolidColorBrush PersistentBorder = Frozen(0x9A, 0xA6, 0xB8);
    private static readonly SolidColorBrush PooledFill = Frozen(0xFC, 0xF3, 0xE3);
    private static readonly SolidColorBrush PooledBorder = Frozen(0xC6, 0xA8, 0x7A);
    private static readonly SolidColorBrush SubGraphFill = Frozen(0xE6, 0xF7, 0xE9);
    private static readonly SolidColorBrush SubGraphBorder = Frozen(0x8F, 0xBF, 0x98);
    private static readonly SolidColorBrush BoundaryFill = Frozen(0xEF, 0xEF, 0xEF);
    private static readonly SolidColorBrush BoundaryBorder = Frozen(0xAA, 0xAA, 0xAA);
    private static readonly SolidColorBrush MissingBorder = Frozen(0xC0, 0x8A, 0x00);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool border = string.Equals(parameter as string, "border", StringComparison.OrdinalIgnoreCase);
        if (value is not DominoNodeViewModel node)
        {
            return border ? PersistentBorder : PersistentFill;
        }

        if (border && node.SignatureMissing)
        {
            return MissingBorder;
        }

        return node switch
        {
            { IsBoundary: true } => border ? BoundaryBorder : BoundaryFill,
            { IsSubGraph: true } => border ? SubGraphBorder : SubGraphFill,
            { IsPooled: true } => border ? PooledBorder : PooledFill,
            _ => border ? PersistentBorder : PersistentFill,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
