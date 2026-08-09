using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JackAll.App.Domino;

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
    private static readonly SolidColorBrush Control = Frozen(0x5A, 0x64, 0x72);
    private static readonly SolidColorBrush Undeclared = Frozen(0xC0, 0x8A, 0x00);
    private static readonly SolidColorBrush Unknown = Frozen(0x78, 0x78, 0x78);

    private static readonly Dictionary<string, SolidColorBrush> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nomad|entity"] = Frozen(0x2E, 0x7D, 0x32),      // green - by far the most common
        ["Core|string"] = Frozen(0xA8, 0x43, 0x00),       // burnt orange
        ["Core|int"] = Frozen(0x15, 0x65, 0xC0),          // blue
        ["Core|float"] = Frozen(0x00, 0x83, 0x8F),        // teal
        ["Core|bool"] = Frozen(0x6A, 0x1B, 0x9A),         // purple
        ["Nomad|animation"] = Frozen(0xAD, 0x14, 0x57),   // magenta
        ["Nomad|Sound"] = Frozen(0xB7, 0x8A, 0x00),
        ["Nomad|SoundType"] = Frozen(0xB7, 0x8A, 0x00),
        ["Nomad|SoundMixing"] = Frozen(0xB7, 0x8A, 0x00),
        ["Nomad|texture"] = Frozen(0x5D, 0x40, 0x37),
        ["Core|boxclass"] = Frozen(0x45, 0x5A, 0x64),
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
