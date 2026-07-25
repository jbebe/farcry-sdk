using System.Text;
using System.Windows.Controls;
using JackAll.Core.Format;

namespace JackAll.App.FileHandlers.DepLoad;

/// <summary>
/// The file handler for `_depload.dat` - the container's own row preview. Read-only, decode-only (a
/// dependency link is "just a link," see docs/docs/file-formats/depload.md): the real browsable tree
/// of parent/child links lives in the file explorer itself, as synthetic rows nested under this file
/// (mirroring how a splitting `.fcb`'s fragments nest under it - see
/// <see cref="JackAll.Core.Vfs.GameVfs"/>'s dependency-link merge pass), so this preview is just a
/// compact summary, not the interactive tree.
/// </summary>
public partial class DepLoadFileHandler : UserControl
{
    public DepLoadFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        Load(fileName, content);
    }

    private void Load(string fileName, byte[] content)
    {
        try
        {
            DepLoadFile depLoad = DepLoadDocument.Decode(content);
            int childCount = depLoad.Parents.Sum(p => p.Children.Count);
            StatusText.Text =
                $"{fileName}\n\n" +
                $"{depLoad.Parents.Count:N0} parent(s), {childCount:N0} child dependencies total.\n\n" +
                "Expand this file in the tree to browse each link and jump to what it references.";
            Outline.Text = BuildOutline(depLoad);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            Outline.Text = string.Empty;
        }
    }

    private static string BuildOutline(DepLoadFile depLoad)
    {
        var sb = new StringBuilder();
        foreach (DepLoadParent parent in depLoad.Parents)
        {
            sb.AppendLine($"0x{parent.Hash:X8}");
            foreach (DepLoadChild child in parent.Children)
            {
                sb.AppendLine($"    -> 0x{child.Hash:X8}");
            }
        }
        return sb.ToString();
    }
}
