using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Data;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace JackAll.App.FileHandlers.Text;

/// <summary>
/// Resolves the AvalonEdit highlighting definition for a file extension. "xml" uses the definition
/// AvalonEdit ships with; "lua" loads the .xshd embedded in this assembly.
/// </summary>
public sealed class ExtensionToHighlightingConverter : IValueConverter
{
    private static readonly Lazy<IHighlightingDefinition> Lua = new(LoadLua);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        // .mgb.desc is plain XML under a non-".xml" name (see FileTypeSniffer) - same highlighting.
        "xml" or "desc" or "mgb.desc" => HighlightingManager.Instance.GetDefinitionByExtension(".xml"),
        "lua" => Lua.Value,
        _ => null,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IHighlightingDefinition LoadLua()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("JackAll.App.FileHandlers.Text.Lua.xshd")
            ?? throw new InvalidOperationException("Lua.xshd is missing from the assembly's embedded resources.");

        using var reader = new XmlTextReader(stream);
        return HighlightingLoader.Load(HighlightingLoader.LoadXshd(reader), HighlightingManager.Instance);
    }
}
