using JackAll.Tools.Format;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JackAll.App.FileHandlers.Mgb;

/// <summary>
/// The file handler for .mgb (Magma UI binary) files: a tree view of the header/type-table (fully
/// byte-verified) and the decoded widget/animation tree (see <see cref="MgbBody"/>), plus a "Mod
/// Configuration Menu tools" panel for the two edit operations built for that goal
/// (<see cref="MgbFileBuilder"/>/<see cref="MgbPageEditor"/> - see
/// docs/docs/engine-internals/magma-menu-system.md and docs/docs/file-formats/mgb.md): build a brand
/// new Page full of CheckBox rows from scratch, or splice a new Button into an already-reachable
/// top-level Page. A real file's tree often stops partway through - <see cref="MgbTypeTable"/> doesn't
/// yet name every class the format can reference, and once an unnamed class is hit, decoding can't
/// safely continue past it (the reader's position can't be trusted without knowing that class's field
/// layout). That's shown as a clear stopping point, not silently hidden or crashed past - and it's also
/// exactly why "Add a Button" only works up to whatever top-level Page(s) were actually reached before
/// any such stop.
/// </summary>
public partial class MgbFileHandler : UserControl
{
    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private byte[] _content;
    private List<MgbAreaLocation> _topLevelPages = [];

    public MgbFileHandler(string fileName, byte[] content, Action<byte[]> replaceContent)
    {
        InitializeComponent();
        _fileName = fileName;
        _replaceContent = replaceContent;
        _content = content;
        Load(content);
    }

    private void Load(byte[] content)
    {
        _content = content;
        Tree.Items.Clear();
        _topLevelPages = [];

        try
        {
            MgbHeader header = MgbHeader.Decode(content);
            var trace = new List<MgbAreaLocation>();
            MgbNode body = MgbBody.ParsePackage(content, header, trace);
            _topLevelPages = trace.Where(t => t.IsTopLevel && t.Kind == "Page").ToList();

            HeaderText.Text = BuildHeaderSummary(_fileName, header, content.Length);
            Tree.Items.Add(BuildTreeItem(body));

            PageLocationsText.Text = _topLevelPages.Count == 0
                ? "No fully-parsed top-level Page found in this file (either it genuinely has none, or " +
                  "the parser stopped before reaching one - see the summary above)."
                : $"{_topLevelPages.Count} top-level Page(s) reachable - use index 0 to {_topLevelPages.Count - 1}.";
            AddButtonButton.IsEnabled = _topLevelPages.Count > 0;
        }
        catch (Exception ex)
        {
            HeaderText.Text = $"{_fileName}\n\nCouldn't read this file: {ex.Message}";
            PageLocationsText.Text = "Unavailable - this file didn't parse at all.";
            AddButtonButton.IsEnabled = false;
        }

        ToolsStatusText.Text = string.Empty;
    }

    private static string BuildHeaderSummary(string fileName, MgbHeader header, int fileLength)
    {
        int resolved = header.Types.Count(t => t.Name is not null);
        return $"{fileName}\n\n" +
               "Magic:        MAGMA\n" +
               $"Version:      0x{header.Version:X6}\n" +
               $"Flag byte:    0x{header.FlagByte:X2} (purpose not identified)\n" +
               $"Header size:  0x{header.HeaderLength:X} bytes\n" +
               $"Body:         {fileLength - header.HeaderLength:N0} bytes\n" +
               $"Type table:   {header.Types.Count} entries ({resolved} resolved to a known class name)";
    }

    private static TreeViewItem BuildTreeItem(MgbNode node)
    {
        var item = new TreeViewItem { Header = node.Kind, IsExpanded = true };
        foreach (MgbField field in node.Fields)
        {
            item.Items.Add(new TreeViewItem
            {
                Header = $"{field.Label} = {field.Value}",
                Foreground = Brushes.DimGray,
                FontStyle = FontStyles.Italic,
            });
        }
        foreach (MgbNode child in node.Children)
        {
            item.Items.Add(BuildTreeItem(child));
        }
        return item;
    }

    private void BuildModsPageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ushort.TryParse(PageWidthBox.Text.Trim(), out ushort width) ||
                !ushort.TryParse(PageHeightBox.Text.Trim(), out ushort height))
            {
                throw new FormatException("Page width/height must be whole numbers.");
            }

            var rows = new List<MgbFileBuilder.ModCheckBoxRow>();
            foreach (string rawLine in CheckBoxRowsBox.Text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                int eq = line.LastIndexOf('=');
                if (eq >= 0)
                {
                    string label = line[..eq].Trim();
                    string flag = line[(eq + 1)..].Trim();
                    bool on = flag.Equals("on", StringComparison.OrdinalIgnoreCase)
                        || flag.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || flag == "1";
                    rows.Add(new MgbFileBuilder.ModCheckBoxRow(label, on));
                }
                else
                {
                    rows.Add(new MgbFileBuilder.ModCheckBoxRow(line, false));
                }
            }

            byte[] built = MgbFileBuilder.BuildModsPage("FCSE_ModsPage", (width, height), rows);
            _replaceContent(built);
            Load(built);
            ToolsStatusText.Foreground = Brushes.DarkGreen;
            ToolsStatusText.Text = $"Built a new Mods page with {rows.Count} row(s) and replaced this file's content.";
        }
        catch (Exception ex)
        {
            ToolsStatusText.Foreground = Brushes.DarkRed;
            ToolsStatusText.Text = $"Couldn't build: {ex.Message}";
        }
    }

    private void AddButtonButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(PageIndexBox.Text.Trim(), out int index))
            {
                throw new FormatException("Page index must be a whole number.");
            }
            string label = ButtonLabelBox.Text.Trim();
            if (label.Length == 0)
            {
                throw new InvalidOperationException("Button label can't be empty.");
            }

            byte[] edited = MgbPageEditor.AddButtonToTopLevelPage(_content, index, label, new MgbBox(0, 0, 200, 32));
            _replaceContent(edited);
            Load(edited);
            ToolsStatusText.Foreground = Brushes.DarkGreen;
            ToolsStatusText.Text = $"Added \"{label}\" as a new Button on Page #{index} and replaced this file's content.";
        }
        catch (Exception ex)
        {
            ToolsStatusText.Foreground = Brushes.DarkRed;
            ToolsStatusText.Text = $"Couldn't add button: {ex.Message}";
        }
    }
}
