using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackAll.App.FileHandlers.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Fcb;

/// <summary>
/// The file handler for .fcb entity/weapon/vehicle/world-sector data. Decodes to Gibbed-compatible
/// XML on load, exports that XML for editing, and imports a (possibly hand-edited) XML file back into
/// a replacement .fcb staged into the workspace.
///
/// When the file is modded the Preview shows the same trimmed diff-against-vanilla view as the plain
/// XML/Lua text handler (<see cref="TextFileHandler.ApplyDiff"/>) instead of the full document. That
/// Preview is skipped - neither the diff nor the plain document - when the raw content or base game
/// version is over <see cref="FileHandlerCatalog.MaxPreviewBytes"/>; Export… still works either way,
/// since that cost is unrelated to laying the content out in the editor.
/// </summary>
public partial class FcbFileHandler : UserControl
{
    private static Lazy<FcbClassDefinitions> Definitions => FcbDefinitionsProvider.Value;

    private readonly string _fileName;
    private readonly bool _isModded;
    private readonly Action<byte[]> _replaceContent;
    private readonly Func<byte[]?> _readOriginal;
    private string? _xml;

    public FcbFileHandler(VfsFile file, byte[] content, Action<byte[]> replaceContent, Func<byte[]?> readOriginal)
    {
        InitializeComponent();
        _fileName = file.FileName;
        _isModded = file.IsModded;
        _replaceContent = replaceContent;
        _readOriginal = readOriginal;
        _ = LoadAsync(content);
    }

    private async Task LoadAsync(byte[] content)
    {
        StatusText.Text = $"{_fileName}\n\nDecoding…";
        ExportButton.IsEnabled = false;
        _xml = null;

        try
        {
            (string xml, string? originalXml, bool tooLargeToPreview) = await Task.Run(() =>
            {
                FcbObject root = FcbDocument.Deserialize(content);
                string xml = FcbXml.ToXml(root, Definitions.Value);

                // Measured on the rendered XML, not the .fcb: a container expands by more than an
                // order of magnitude, so the raw bytes say nothing about what the editor has to lay
                // out (and what a diff would have to run over).
                bool tooLarge = FileHandlerCatalog.ExceedsPreviewLimit(xml);
                string? originalXml = tooLarge ? null : TryRenderOriginalXml(TryReadOriginalBytes());
                return (xml, originalXml, tooLarge || (originalXml is not null
                    && FileHandlerCatalog.ExceedsPreviewLimit(originalXml)));
            });

            _xml = xml;
            if (tooLargeToPreview)
            {
                // Export still works below - this only skips laying the content out in the editor
                // control (and, for a diff, running a line diff over it), see MaxPreviewBytes' remarks.
                Preview.ShowPlainText(FileHandlerCatalog.TooLargeMessage(xml.Length));
            }
            else if (originalXml is not null)
            {
                Preview.ApplyDiff(originalXml, xml);
            }
            else
            {
                Preview.ShowPlainText(xml);
            }

            StatusText.Text = $"{_fileName}\n\nReady to export.";
            ExportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Preview.ShowPlainText(string.Empty);
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            ExportButton.IsEnabled = false;
        }
    }

    /// <summary>The base-game version's raw bytes, for <see cref="LoadAsync"/> to size-check before
    /// deciding whether to render it at all - or null if the file isn't modded, has no base-game
    /// original, or <see cref="_readOriginal"/> throws (no archive has it anymore).</summary>
    private byte[]? TryReadOriginalBytes()
    {
        if (!_isModded)
        {
            return null;
        }

        try
        {
            return _readOriginal();
        }
        catch
        {
            return null; // no usable base game version to diff against - fall through to plain view
        }
    }

    /// <summary>
    /// <paramref name="originalBytes"/> rendered the same way as the current file, for
    /// <see cref="LoadAsync"/> to diff against - or null if there's nothing to compare against.
    /// Mirrors <see cref="FileHandlerCatalog.BuildTextHandler"/>'s same fallback-to-plain-view logic
    /// for the xml/lua case.
    /// </summary>
    private string? TryRenderOriginalXml(byte[]? originalBytes)
    {
        if (originalBytes is null)
        {
            return null;
        }

        try
        {
            return FcbXml.ToXml(FcbDocument.Deserialize(originalBytes), Definitions.Value);
        }
        catch
        {
            return null; // no usable base game version to diff against - fall through to plain view
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_xml is not { } xml)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export decoded XML to…",
            Filter = "XML file|*.xml",
            FileName = Path.GetFileNameWithoutExtension(_fileName) + ".xml",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            await Task.Run(() => File.WriteAllText(dialog.FileName, xml));

            StatusText.Text += $"\n\nExported to:\n{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't export: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import - select the (possibly edited) exported .xml file",
            Filter = "XML file|*.xml",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ImportButton.IsEnabled = false;
        try
        {
            byte[] combined = await Task.Run(() =>
            {
                FcbObject root = FcbXml.FromXml(File.ReadAllText(dialog.FileName));
                byte[] fcb = FcbDocument.Serialize(root);

                // Round-trips the freshly built file back through Deserialize as a validity check —
                // matches the same sanity check the Xbt/Sbao handlers do before staging.
                FcbDocument.Deserialize(fcb);
                return fcb;
            });

            _replaceContent(combined);
            await LoadAsync(combined);
            StatusText.Text += $"\n\nImported from:\n{dialog.FileName}\n\nStaged in your workspace.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't import: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }
    }
}
