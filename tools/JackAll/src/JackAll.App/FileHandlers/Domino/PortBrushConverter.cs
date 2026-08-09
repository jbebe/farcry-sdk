using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// Colors a port (and the wires leaving it) by what it carries. Control ports share one neutral slate;
/// data ports get a color per declared type, so a glance at a wire tells you whether an entity, a
/// string or a number is moving along it.
///
/// The type vocabulary is closed - eleven values across all 234 node signatures - so this is an
/// exhaustive map rather than a hash-to-hue scheme, and the colors are chosen to stay distinguishable
/// against the light canvas.
/// </summary>
public sealed class PortBrushConverter : IValueConverter
{
    // Deliberately dark. These are label colours as much as dot colours, and the labels sit on the
    // node's own light fill - cream, pale blue, pale green - not on white. Anything lighter than this
    // drops under a 4.5:1 contrast ratio there and stops being comfortably readable; the earlier,
    // brighter palette measured as low as 3.22:1.
    private static readonly SolidColorBrush Control = Frozen(0x3A, 0x42, 0x4D);
    private static readonly SolidColorBrush Undeclared = Frozen(0x7A, 0x58, 0x00);
    private static readonly SolidColorBrush Unknown = Frozen(0x4A, 0x4A, 0x4A);

    private static readonly Dictionary<string, SolidColorBrush> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nomad|entity"] = Frozen(0x1B, 0x5E, 0x20),      // green - by far the most common
        ["Core|string"] = Frozen(0x7A, 0x31, 0x00),       // burnt orange
        ["Core|int"] = Frozen(0x0D, 0x47, 0xA1),          // blue
        ["Core|float"] = Frozen(0x00, 0x56, 0x5E),        // teal
        ["Core|bool"] = Frozen(0x4A, 0x14, 0x8C),         // purple
        ["Nomad|animation"] = Frozen(0x7B, 0x0F, 0x3E),   // magenta
        ["Nomad|Sound"] = Frozen(0x6E, 0x53, 0x00),
        ["Nomad|SoundType"] = Frozen(0x6E, 0x53, 0x00),
        ["Nomad|SoundMixing"] = Frozen(0x6E, 0x53, 0x00),
        ["Nomad|texture"] = Frozen(0x4E, 0x34, 0x2E),
        ["Core|boxclass"] = Frozen(0x2F, 0x3E, 0x46),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DominoConnectorViewModel port)
        {
            return Control;
        }
        if (!port.Declared)
        {
            return Undeclared;
        }
        if (port.Kind == PortKind.Control)
        {
            return Control;
        }
        return port.Type is not null && ByType.TryGetValue(port.Type, out SolidColorBrush? brush) ? brush : Unknown;
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
