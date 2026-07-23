using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackAll.App.FileHandlers;
using JackAll.App.FileHandlers.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Fcb;

/// <summary>
/// The file handler for .fcb entity/weapon/vehicle/world-sector data. Decodes to Gibbed-compatible
/// XML on load (splitting into external sub-files for entity-library-shaped roots, matching Gibbed's
/// own multi-export), exports that XML to a folder for editing, and imports a (possibly hand-edited)
/// folder of XML back into a replacement .fcb staged into the workspace.
///
/// When a root doesn't split (<see cref="FcbXmlExport.ExternalFiles"/> is empty - the file is just one
/// document's worth of content, not an entity library of groups) and the file is modded, the Preview
/// shows the same trimmed diff-against-vanilla view as the plain XML/Lua text handler
/// (<see cref="TextFileHandler.ApplyDiff"/>) instead of the full document - a split root never does,
/// since a line diff of the small index document wouldn't show the real (per sub-file) changes anyway.
/// That same non-split Preview is skipped - neither the diff nor the plain document - when the raw
/// content or base game version is over <see cref="FileHandlerCatalog.MaxPreviewBytes"/>; Export…
/// still works either way, since that cost is unrelated to laying the content out in the editor.
/// </summary>
public partial class FcbFileHandler : UserControl
{
    private static Lazy<FcbClassDefinitions> Definitions => FcbDefinitionsProvider.Value;

    private readonly string _fileName;
    private readonly bool _isModded;
    private readonly Action<byte[]> _replaceContent;
    private readonly Func<byte[]?> _readOriginal;
    private FcbXmlExport? _export;

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
        _export = null;

        try
        {
            (FcbXmlExport export, string? originalXml, bool tooLargeToPreview) = await Task.Run(() =>
            {
                FcbObject root = FcbDocument.Deserialize(content);
                FcbXmlExport export = FcbXml.ToXml(root, Definitions.Value);
                if (export.ExternalFiles.Count != 0)
                {
                    return (export, (string?)null, false);
                }

                byte[]? originalBytes = TryReadOriginalBytes();
                bool tooLarge = FileHandlerCatalog.ExceedsPreviewLimit(content)
                    || (originalBytes is not null && FileHandlerCatalog.ExceedsPreviewLimit(originalBytes));
                string? originalXml = tooLarge ? null : TryRenderOriginalXml(originalBytes);
                return (export, originalXml, tooLarge);
            });

            _export = export;
            if (tooLargeToPreview)
            {
                // Export still works below - this only skips laying the content out in the editor
                // control (and, for a diff, running a line diff over it), see MaxPreviewBytes' remarks.
                Preview.ShowPlainText(FileHandlerCatalog.TooLargeMessage(content.Length));
            }
            else if (originalXml is not null)
            {
                Preview.ApplyDiff(originalXml, export.IndexXml);
            }
            else
            {
                Preview.ShowPlainText(export.IndexXml);
            }

            StatusText.Text =
                $"{_fileName}\n\n" +
                (export.ExternalFiles.Count > 0
                    ? $"Decoded into {export.ExternalFiles.Count:N0} sub-files (written out by Export).\n\n"
                    : string.Empty) +
                "Ready to export.";
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
    /// <paramref name="originalBytes"/> rendered the same un-split way as the current file, for
    /// <see cref="LoadAsync"/> to diff against - or null if there's nothing to (usefully) compare
    /// against: no bytes were passed, or they decode into a root that itself splits into sub-files (a
    /// shape change that makes a direct index-document diff meaningless). Mirrors
    /// <see cref="FileHandlerCatalog.BuildTextHandler"/>'s same fallback-to-plain-view logic for the
    /// xml/lua case.
    /// </summary>
    private string? TryRenderOriginalXml(byte[]? originalBytes)
    {
        if (originalBytes is null)
        {
            return null;
        }

        try
        {
            FcbObject originalRoot = FcbDocument.Deserialize(originalBytes);
            FcbXmlExport originalExport = FcbXml.ToXml(originalRoot, Definitions.Value);
            return originalExport.ExternalFiles.Count == 0 ? originalExport.IndexXml : null;
        }
        catch
        {
            return null; // no usable base game version to diff against - fall through to plain view
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_export is not { } export)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Export decoded XML to…" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            string indexPath = Path.Combine(dialog.FolderName, Path.GetFileNameWithoutExtension(_fileName) + ".xml");
            await Task.Run(() =>
            {
                File.WriteAllText(indexPath, export.IndexXml);
                foreach ((string name, string xml) in export.ExternalFiles)
                {
                    File.WriteAllText(Path.Combine(dialog.FolderName, name), xml);
                }
            });

            StatusText.Text += $"\n\nExported to:\n{dialog.FolderName}";
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
            Title = "Import - select the (possibly edited) exported .xml index file",
            Filter = "XML file|*.xml",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ImportButton.IsEnabled = false;
        try
        {
            string folder = Path.GetDirectoryName(dialog.FileName)!;
            byte[] combined = await Task.Run(() =>
            {
                string indexXml = File.ReadAllText(dialog.FileName);
                FcbObject root = FcbXml.FromXml(indexXml, name => File.ReadAllText(Path.Combine(folder, name)));
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
