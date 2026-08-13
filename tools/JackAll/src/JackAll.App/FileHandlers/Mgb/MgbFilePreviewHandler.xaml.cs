using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackAll.Tools.Mgb;

namespace JackAll.App.FileHandlers.Mgb;

/// <summary>
/// The Files tab's compact preview for a <c>.mgb</c> Magma UI package: the "Open in MGB Editor…"
/// launcher plus the one-line header summary, mirroring <see cref="LauncherPreviewHandler"/>'s
/// launcher-plus-preview shape (a package summary rather than a text diff, so not that class). The editor itself
/// (<see cref="App.Mgb.MgbTabView"/>) needs a tab of its own - see its remarks.
/// </summary>
public partial class MgbFilePreviewHandler : UserControl
{
    private readonly Action _openEditor;

    public MgbFilePreviewHandler(byte[] content, Action openEditor)
    {
        InitializeComponent();
        _openEditor = openEditor;

        try
        {
            SummaryText.Text = MgbPackage.Read(content).Describe(content.Length);
        }
        catch (Exception ex)
        {
            // Nothing the editor could show either, so don't offer to open an empty tree - the reason
            // it failed is more useful here.
            SummaryText.Text = $"Couldn't read this file: {ex.Message}";
            SummaryText.Foreground = Brushes.DarkRed;
            OpenButton.IsEnabled = false;
        }
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e) => _openEditor();
}
