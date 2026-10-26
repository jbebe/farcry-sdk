using System.Windows.Media;

namespace JackAll.App;

/// <summary>An RGB value from 0 to 1 as a brush; a channel outside that range shows clamped.</summary>
public static class ColourSwatch
{
    public static SolidColorBrush Of(float[] rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb(Byte(rgb[0]), Byte(rgb[1]), Byte(rgb[2])));
        brush.Freeze();
        return brush;
    }

    private static byte Byte(float channel) => (byte)Math.Clamp(channel * 255f, 0f, 255f);
}
