using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

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
    private static readonly Brush AddedBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xFF, 0xED)));
    private static readonly Brush RemovedBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xF0)));
    private static readonly Brush AddedForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x66, 0x1E)));
    private static readonly Brush RemovedForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x1F, 0x11)));
    private static readonly Brush GapForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
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
                ChangeLinePart(line.Offset, line.EndOffset, e =>
                {
                    e.TextRunProperties.SetBackgroundBrush(AddedBackground);
                    e.TextRunProperties.SetForegroundBrush(AddedForeground);
                });
                break;

            case DiffLineKind.Removed:
                ChangeLinePart(line.Offset, line.EndOffset, e =>
                {
                    e.TextRunProperties.SetBackgroundBrush(RemovedBackground);
                    e.TextRunProperties.SetForegroundBrush(RemovedForeground);
                    e.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                });
                break;

            case DiffLineKind.Gap:
                ChangeLinePart(line.Offset, line.EndOffset, e =>
                {
                    e.TextRunProperties.SetForegroundBrush(GapForeground);
                    e.TextRunProperties.SetTypeface(GapTypeface);
                });
                break;
        }
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
