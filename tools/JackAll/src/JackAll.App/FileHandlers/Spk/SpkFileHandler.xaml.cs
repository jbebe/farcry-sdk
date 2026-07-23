using System.Text;
using System.Windows.Controls;
using JackAll.Core.Format;

namespace JackAll.App.FileHandlers.Spk;

/// <summary>
/// The file handler for .spk sound-bank containers - read-only. Shows the record count and, per
/// record, its id, preamble words, and payload size - the container structure confirmed by tracing
/// Dunia.dll's real (non-stub) sound-bank loader in Ghidra (see <see cref="SpkPackage"/>'s remarks).
/// Each payload is shown as a hex preview rather than decoded: its own internal layout is registered
/// by the engine as an opaque {id, pointer, size} triple at load time and only interpreted later, by
/// whatever actually triggers playback - a boundary this preview doesn't cross.
/// </summary>
public partial class SpkFileHandler : UserControl
{
    private const int PayloadPreviewBytes = 32;

    public SpkFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        Load(fileName, content);
    }

    private void Load(string fileName, byte[] content)
    {
        try
        {
            SpkPackage package = SpkPackage.Parse(content);

            var sb = new StringBuilder();
            sb.AppendLine(fileName);
            sb.AppendLine();
            sb.AppendLine($"Records: {package.Records.Count}");
            sb.AppendLine();
            sb.AppendLine("Payload contents aren't decoded here - the engine registers each one as an");
            sb.AppendLine("opaque {id, pointer, size} triple at load time; only a hex preview is shown.");
            sb.AppendLine();

            for (int i = 0; i < package.Records.Count; i++)
            {
                SpkRecord r = package.Records[i];
                sb.AppendLine($"[{i}] id=0x{r.Id:x8}  preamble=[{string.Join(", ", r.PreambleWords.Select(w => $"0x{w:x8}"))}]  size={r.Payload.Length:N0}");
                int previewLen = Math.Min(PayloadPreviewBytes, r.Payload.Length);
                string hex = string.Join(" ", r.Payload.Take(previewLen).Select(b => b.ToString("x2")));
                sb.AppendLine($"      {hex}{(previewLen < r.Payload.Length ? " ..." : "")}");
            }

            StatusText.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }
}
