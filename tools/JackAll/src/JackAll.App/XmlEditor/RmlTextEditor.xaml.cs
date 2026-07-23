using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting;

namespace JackAll.App.XmlEditor;

/// <summary>
/// A compact, editable, XML-syntax-highlighted view for a Rml-typed property grid field (see
/// FcbFieldFormat) - unlike FileHandlers.Text.TextFileHandler's read-only viewer, this one has to
/// push edits back out, so <see cref="Text"/> binds both ways.
/// </summary>
public partial class RmlTextEditor : UserControl
{
    // Not measured off the editor's own TextView (DefaultLineHeight isn't reliable before the control
    // has actually been laid out once) - tuned by eye instead, for the FontSize this control's XAML
    // hard-codes.
    private const double LineHeight = 18;
    private const int MaxVisibleLines = 15;

    // TextEditor.Text isn't a DependencyProperty (it just forwards to Document), so it can't be a
    // XAML binding target directly - OnTextPropertyChanged below pushes host -> editor, and the
    // TextChanged handler wired in the constructor pushes editor -> host.
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(RmlTextEditor),
        new FrameworkPropertyMetadata(
            string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

    private bool _suppressTextChanged;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public RmlTextEditor()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".xml");
        Editor.TextChanged += (_, _) =>
        {
            UpdateHeight();

            if (_suppressTextChanged) return;

            // SetCurrentValue (not SetValue) so this doesn't fight the Text="{Binding ...}" binding
            // ScalarField's DataTemplate sets up - same reason a Slider updates Value this way while
            // the user drags it.
            SetCurrentValue(TextProperty, Editor.Text);
        };
    }

    /// <summary>Grows with the document up to <see cref="MaxVisibleLines"/>, then leaves the rest to
    /// the editor's own scrollbar - a short value (a handful of attributes) shouldn't reserve the same
    /// vertical space as a deeply nested one. Runs on every text change regardless of direction (both
    /// the host setting <see cref="Text"/> and the user typing go through Editor.TextChanged).</summary>
    private void UpdateHeight() => Editor.Height = Math.Min(Editor.Document.LineCount, MaxVisibleLines) * LineHeight;

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RmlTextEditor)d;
        var newText = (string)e.NewValue;
        if (control.Editor.Text == newText) return;

        // Round-tripping through the editor on every keystroke (Editor fires TextChanged ->
        // SetCurrentValue -> this callback) would otherwise reset the caret to 0 mid-typing.
        control._suppressTextChanged = true;
        int caret = control.Editor.CaretOffset;
        control.Editor.Text = newText;
        control.Editor.CaretOffset = Math.Min(caret, newText.Length);
        control._suppressTextChanged = false;
    }
}
