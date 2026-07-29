using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// The Files tab's compact preview for a `domino\user\*.lua` mission-graph script: the "Open in Domino
/// Editor…" launcher on top (the graph reconstruction and canvas rendering are a job for the dedicated
/// tab, <see cref="App.Domino.DominoTabView"/>, not this column) plus, underneath, the same
/// trimmed diff-against-vanilla or plain-text view any other modded/unmodded text file gets - mirrors
/// <see cref="Fcb.FcbFragmentDetailsHandler"/>'s launcher-plus-preview shape.
/// </summary>
public partial class DominoFilePreviewHandler : UserControl
{
    private readonly Action _openEditor;

    public DominoFilePreviewHandler(Action openEditor, string? currentText, string? originalText, string? previewUnavailableText)
    {
        InitializeComponent();
        _openEditor = openEditor;

        if (currentText is null)
        {
            PreviewBorder.Visibility = Visibility.Collapsed;
            PreviewUnavailableNotice.Text = previewUnavailableText;
            PreviewUnavailableNotice.Visibility = Visibility.Visible;
            return;
        }

        if (originalText is not null)
        {
            Preview.ApplyDiff(originalText, currentText);
        }
        else
        {
            Preview.ShowPlainText(currentText);
        }
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e) => _openEditor();
}
