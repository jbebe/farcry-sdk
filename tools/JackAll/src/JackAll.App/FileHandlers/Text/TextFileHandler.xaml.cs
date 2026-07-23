using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.FileHandlers.Text;

/// <summary>Read-only, syntax-highlighted text viewer — the file handler for plain-text formats like XML and Lua.</summary>
public partial class TextFileHandler : UserControl
{
    // TextEditor.Text isn't a DependencyProperty (it just forwards to Document), so it can't be a
    // XAML binding target — this callback pushes the value across from code instead.
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TextFileHandler),
        new PropertyMetadata(string.Empty, (d, e) => ((TextFileHandler)d).Editor.Text = (string)e.NewValue));

    public static readonly DependencyProperty ExtensionProperty =
        DependencyProperty.Register(nameof(Extension), typeof(string), typeof(TextFileHandler));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Drives which highlighting definition Editor.xaml picks — see ExtensionToHighlightingConverter.</summary>
    public string Extension
    {
        get => (string)GetValue(ExtensionProperty);
        set => SetValue(ExtensionProperty, value);
    }

    public TextFileHandler() => InitializeComponent();

    /// <summary>
    /// The trimmed, color-coded view for a modded text file (see <see cref="DiffTextBuilder"/>/
    /// <see cref="DiffLineColorizer"/>) - every changed line plus a little context, everything else
    /// collapsed behind a marker line.
    /// </summary>
    public static TextFileHandler CreateDiffView(string originalText, string currentText, string extension)
    {
        var handler = new TextFileHandler { Extension = extension };
        handler.ApplyDiff(originalText, currentText);
        return handler;
    }

    /// <summary>
    /// Switches this viewer into the trimmed diff mode - see <see cref="CreateDiffView"/>. Exposed as an
    /// instance method (rather than just the static factory above) so a host that embeds a long-lived
    /// <see cref="TextFileHandler"/> instance, like <c>FcbFileHandler</c>'s Preview, can re-apply it
    /// across reloads (e.g. after an Import) without leaking a new <see cref="DiffLineColorizer"/> onto
    /// the editor each time.
    /// </summary>
    public void ApplyDiff(string originalText, string currentText)
    {
        Editor.TextArea.TextView.LineTransformers.Clear();
        IReadOnlyList<DiffLine> diffLines = DiffTextBuilder.BuildTrimmedDiff(originalText, currentText);
        Text = string.Join(Environment.NewLine, diffLines.Select(l => l.Text));
        Editor.ShowLineNumbers = false;
        DiffBanner.Visibility = Visibility.Visible;
        Editor.TextArea.TextView.LineTransformers.Add(new DiffLineColorizer(diffLines));
    }

    /// <summary>Switches this viewer back to a plain, undecorated view of <paramref name="text"/> - the
    /// counterpart to <see cref="ApplyDiff"/> for a long-lived instance being reloaded.</summary>
    public void ShowPlainText(string text)
    {
        Editor.TextArea.TextView.LineTransformers.Clear();
        Editor.ShowLineNumbers = true;
        DiffBanner.Visibility = Visibility.Collapsed;
        Text = text;
    }
}
