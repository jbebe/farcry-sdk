using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using JackAll.Core.Text;

namespace JackAll.App.FileHandlers.Text;

/// <summary>
/// Colors each visual line of a <see cref="TextFileHandler"/>'s editor by its <see cref="DiffLineKind"/>
/// - the WPF-side half of the trimmed diff view <see cref="DiffTextBuilder"/> computes (see
/// <see cref="TextFileHandler.CreateDiffView"/>). Line numbers are switched off for a diff view (they'd
/// otherwise number the trimmed excerpt, not the real file), so color is the only cue - syntax
/// highlighting stays layered underneath since this only touches background/foreground, never the text.
/// </summary>
internal sealed class DiffLineColorizer(IReadOnlyList<DiffLine> lines) : DocumentColorizingTransformer
{
    private static readonly Typeface GapTypeface =
        new(new FontFamily("Consolas"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

    protected override void ColorizeLine(DocumentLine line)
    {
        int index = line.LineNumber - 1;
        if (index < 0 || index >= lines.Count)
        {
            return;
        }

        switch (lines[index].Kind)
        {
            case DiffLineKind.Added:
                Paint(line, Themed("DiffAddedBgBrush"), Themed("DiffAddedTextBrush"));
                break;

            case DiffLineKind.Removed:
                Paint(line, Themed("DiffRemovedBgBrush"), Themed("DiffRemovedTextBrush"),
                    e => e.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough));
                break;

            case DiffLineKind.Gap:
                Paint(line, null, Themed("TextMutedBrush"), e => e.TextRunProperties.SetTypeface(GapTypeface));
                break;
        }
    }

    private void Paint(DocumentLine line, Brush? background, Brush foreground, Action<VisualLineElement>? extra = null)
        => ChangeLinePart(line.Offset, line.EndOffset, e =>
        {
            if (background is not null) e.TextRunProperties.SetBackgroundBrush(background);
            e.TextRunProperties.SetForegroundBrush(foreground);
            extra?.Invoke(e);
        });

    /// <summary>Looked up per line, not per run of text and not once for all: a theme switch repaints the diff.</summary>
    private static Brush Themed(string key) => (Brush)Application.Current.FindResource(key);
}
