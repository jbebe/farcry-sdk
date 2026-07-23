using System.Text;
using System.Windows.Controls;
using JackAll.Core.Format;

namespace JackAll.App.FileHandlers.Mgb;

/// <summary>
/// The file handler for .mgb (Magma UI binary) files - read-only. Shows the header/type-table (fully
/// byte-verified) and the decoded widget/animation tree (see <see cref="MgbBody"/>). A real file's
/// tree often stops partway through - <see cref="MgbTypeTable"/> doesn't yet name every class the
/// format can reference (see reverse/dunia/mgb_format.md), and once an unnamed class is hit, decoding
/// can't safely continue past it (the reader's position can't be trusted without knowing that class's
/// field layout). That's shown as a clear stopping point, not silently hidden or crashed past.
/// </summary>
public partial class MgbFileHandler : UserControl
{
    public MgbFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        Load(fileName, content);
    }

    private void Load(string fileName, byte[] content)
    {
        try
        {
            MgbHeader header = MgbHeader.Decode(content);
            MgbNode body = MgbBody.ParsePackage(content, header);

            var sb = new StringBuilder();
            AppendHeaderSummary(sb, fileName, header, content.Length);
            sb.AppendLine();
            sb.AppendLine("--- Decoded tree ---");
            sb.AppendLine();
            AppendNode(sb, body, 0);
            StatusText.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }

    private static void AppendHeaderSummary(StringBuilder sb, string fileName, MgbHeader header, int fileLength)
    {
        sb.AppendLine(fileName);
        sb.AppendLine();
        sb.AppendLine("Magic:        MAGMA");
        sb.AppendLine($"Version:      0x{header.Version:X6}");
        sb.AppendLine($"Flag byte:    0x{header.FlagByte:X2} (purpose not identified)");
        sb.AppendLine($"Header size:  0x{header.HeaderLength:X} bytes");
        sb.AppendLine($"Body:         {fileLength - header.HeaderLength:N0} bytes");

        int resolved = header.Types.Count(t => t.Name is not null);
        sb.AppendLine($"Type table:   {header.Types.Count} entries ({resolved} resolved to a known class name)");
    }

    private static void AppendNode(StringBuilder sb, MgbNode node, int depth)
    {
        string indent = new(' ', depth * 2);
        sb.AppendLine($"{indent}{node.Kind}");
        foreach (MgbField field in node.Fields)
        {
            sb.AppendLine($"{indent}  {field.Label} = {field.Value}");
        }
        foreach (MgbNode child in node.Children)
        {
            AppendNode(sb, child, depth + 1);
        }
    }
}
