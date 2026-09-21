using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace JackAll.App.FileHandlers.Text;

/// <summary>The AvalonEdit highlighting for a file extension, in the current theme. "xml" is the
/// definition AvalonEdit ships, "lua" the .xshd embedded in this assembly; the dark theme loads the
/// same definitions with every colour too dark for a dark ground lifted towards white.</summary>
internal static class SyntaxTheme
{
    private const string XmlResource = "ICSharpCode.AvalonEdit.Highlighting.Resources.XML-Mode.xshd";
    private const string LuaResource = "JackAll.App.FileHandlers.Text.Lua.xshd";

    private static readonly Dictionary<(string Resource, bool Dark), IHighlightingDefinition> Cache = [];

    public static IHighlightingDefinition? For(string? extension) => extension switch
    {
        // .mgb.desc is plain XML under a non-".xml" name (see FileTypeSniffer) - same highlighting.
        "xml" or "desc" or "mgb.desc" => Load(typeof(TextEditor).Assembly, XmlResource),
        "lua" => Load(Assembly.GetExecutingAssembly(), LuaResource),
        _ => null,
    };

    /// <summary>Highlights <paramref name="editor"/> when it loads and again on every theme change,
    /// for as long as it is loaded. A diff view's line colours repaint with it.</summary>
    public static void Bind(TextEditor editor, Func<string?> extension)
    {
        void Apply()
        {
            editor.SyntaxHighlighting = For(extension());
            editor.TextArea.TextView.Redraw();
        }

        editor.Loaded += (_, _) =>
        {
            ThemeManager.Changed -= Apply;
            ThemeManager.Changed += Apply;
            Apply();
        };
        editor.Unloaded += (_, _) => ThemeManager.Changed -= Apply;
    }

    private static IHighlightingDefinition Load(Assembly assembly, string resource)
    {
        if (Cache.TryGetValue((resource, ThemeManager.IsDark), out IHighlightingDefinition? cached))
        {
            return cached;
        }

        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} is missing from {assembly.GetName().Name}.");
        using var reader = new XmlTextReader(stream);
        XshdSyntaxDefinition xshd = HighlightingLoader.LoadXshd(reader);

        if (ThemeManager.IsDark)
        {
            // Both definitions declare every colour by name at the top level; rules only refer to them.
            foreach (XshdColor color in xshd.Elements.OfType<XshdColor>())
            {
                if (color.Foreground?.GetColor(null) is Color foreground)
                {
                    color.Foreground = new SimpleHighlightingBrush(Lift(foreground));
                }
            }
        }

        return Cache[(resource, ThemeManager.IsDark)] = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
    }

    /// <summary>Blends a colour whose luminance would sink into a dark ground halfway to white.</summary>
    private static Color Lift(Color c)
    {
        double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        if (luminance >= 0.45) return c;

        const double blend = 0.55;
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * blend),
            (byte)(c.G + (255 - c.G) * blend),
            (byte)(c.B + (255 - c.B) * blend));
    }
}
