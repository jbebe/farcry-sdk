using System.Windows;

namespace JackAll.App;

/// <summary>What a deploy has to say - its conflicts and notes, or why it failed - as one selectable text.</summary>
public partial class DeployReportWindow : Window
{
    public DeployReportWindow(string title, string heading, IEnumerable<string> lines)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        ReportText.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => ReportText.Focus();
    }
}
