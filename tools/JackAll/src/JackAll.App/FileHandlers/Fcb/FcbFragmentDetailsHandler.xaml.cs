using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.FileHandlers.Fcb;

/// <summary>
/// The Files tab's preview for a `.fcb` fragment row: the "Open in XML Editor…" launcher on top -
/// fragments can be huge and need real navigation to be useful, which is a job for a dedicated editor
/// tab (<see cref="JackAll.App.XmlEditor.XmlEditorTabView"/>), not this compact detail column - plus,
/// underneath it, either the trimmed diff-against-vanilla view (<see cref="Text.TextFileHandler.ApplyDiff"/>)
/// a modded fragment gets, or its full content when it's modded but has no base game version to diff
/// against (a mod-added fragment). Nothing is shown - just <paramref name="previewUnavailableText"/> in
/// its place, via the constructor below - when the fragment is unmodified, or its content (or the base
/// game version it would diff against) is over <see cref="FileHandlers.FileHandlerCatalog.MaxPreviewBytes"/>:
/// same "fragments can be huge" reasoning as the editor-tab hand-off, just for two different reasons.
/// </summary>
public partial class FcbFragmentDetailsHandler : UserControl
{
    private readonly Action _openEditor;

    public FcbFragmentDetailsHandler(Action openEditor, string? currentXml, string? originalXml, string? previewUnavailableText)
    {
        InitializeComponent();
        _openEditor = openEditor;

        if (currentXml is null)
        {
            PreviewBorder.Visibility = Visibility.Collapsed;
            PreviewUnavailableNotice.Text = previewUnavailableText;
            PreviewUnavailableNotice.Visibility = Visibility.Visible;
            return;
        }

        if (originalXml is not null)
        {
            Preview.ApplyDiff(originalXml, currentXml);
        }
        else
        {
            Preview.ShowPlainText(currentXml);
        }
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e) => _openEditor();
}
