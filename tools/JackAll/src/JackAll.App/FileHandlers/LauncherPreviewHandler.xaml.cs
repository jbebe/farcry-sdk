using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.FileHandlers;

/// <summary>
/// The Files tab's compact "open the real editor" preview: an explanatory blurb, a launcher button
/// handing off to a dedicated editor tab, and underneath either the trimmed diff-against-vanilla /
/// plain-text view any text file gets, or a one-line notice when there's nothing useful to show
/// (unmodified and huge, over the preview size limit, unreadable). Serves both the `.fcb` fragment
/// rows and the `domino\user\*.lua` mission graphs — see
/// <see cref="FileHandlerCatalog.BuildLauncherPreview"/> for who shows what.
/// </summary>
public partial class LauncherPreviewHandler : UserControl
{
    private readonly Action _openEditor;

    public LauncherPreviewHandler(
        string blurb, string buttonText, string extension, Action openEditor,
        string? currentText, string? originalText, string? previewUnavailableText)
    {
        InitializeComponent();
        _openEditor = openEditor;
        Blurb.Text = blurb;
        OpenButton.Content = buttonText;
        Preview.Extension = extension;

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
