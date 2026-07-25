using System.Windows;
using System.Windows.Controls;
using JackAll.Core.Vfs;

namespace JackAll.App.FileHandlers.DepLoad;

/// <summary>
/// The file handler for one dependency-link row — a single `depload.dat` parent or child entry,
/// synthesized by <see cref="GameVfs"/>'s dependency-link merge pass the same way an `.fcb` fragment
/// row is synthesized. A link is a reference, not content (see docs/docs/file-formats/depload.md), so
/// this has no preview/edit surface of its own — just what it points to and a single jump button.
/// </summary>
public partial class DependencyLinkHandler : UserControl
{
    private readonly VfsFile? _target;
    private readonly Action<VfsFile> _navigateTo;

    public DependencyLinkHandler(VfsFile link, VfsFile? target, Action<VfsFile> navigateTo)
    {
        InitializeComponent();
        _target = target;
        _navigateTo = navigateTo;

        string kind = link.LinkChildTypeHash is null ? "Parent" : "Child";
        var lines = new List<string>
        {
            $"{kind} dependency link",
            string.Empty,
            $"Hash: 0x{link.LinkTargetHash:X8}",
            target is not null ? $"Resolved: {target.Path}" : "Not resolved - no archive/mod entry has this hash.",
        };
        if (link.LinkChildTypeHash is { } typeHash) lines.Add($"Type hash: 0x{typeHash:X8}");

        StatusText.Text = string.Join("\n", lines);
        GoToFileButton.IsEnabled = target is not null;
    }

    private void GoToFile_Click(object sender, RoutedEventArgs e)
    {
        if (_target is { } target)
        {
            _navigateTo(target);
        }
    }
}
