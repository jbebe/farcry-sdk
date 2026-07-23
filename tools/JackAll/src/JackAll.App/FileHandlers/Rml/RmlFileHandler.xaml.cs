using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;
using JackAll.Core.Vfs;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Rml;

/// <summary>
/// The file handler for .rml resource-manifest/localization XML (research/file_manifest.md §10) -
/// decodes to plain XML on load, exports that XML to a file for editing, and imports a (possibly
/// hand-edited) file back into a replacement .rml staged into the workspace. Simpler than
/// <see cref="Fcb.FcbFileHandler"/>: unlike .fcb, .rml never splits into external sub-files, so there's
/// just one XML document in and out.
///
/// When the file is modded and has a base game version to compare against, the Preview shows the same
/// trimmed diff-against-vanilla view as the plain XML/Lua text handler
/// (<see cref="Text.TextFileHandler.ApplyDiff"/>) instead of the full document - an override is
/// meaningful by what it changes, not by the (often large, mostly identical) whole file it replaces.
/// </summary>
public partial class RmlFileHandler : UserControl
{
    private readonly string _fileName;
    private readonly bool _isModded;
    private readonly Action<byte[]> _replaceContent;
    private readonly Func<byte[]?> _readOriginal;
    private string? _xml;

    public RmlFileHandler(VfsFile file, byte[] content, Action<byte[]> replaceContent, Func<byte[]?> readOriginal)
    {
        InitializeComponent();
        _fileName = file.FileName;
        _isModded = file.IsModded;
        _replaceContent = replaceContent;
        _readOriginal = readOriginal;
        Load(content);
    }

    private void Load(byte[] content)
    {
        try
        {
            XElement root = RmlDocument.Deserialize(content);
            _xml = root.ToString();

            string? originalXml = TryRenderOriginalXml();
            if (originalXml is not null)
            {
                Preview.ApplyDiff(originalXml, _xml);
            }
            else
            {
                Preview.ShowPlainText(_xml);
            }

            StatusText.Text = $"{_fileName}\n\nReady to export.";
            ExportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _xml = null;
            Preview.ShowPlainText(string.Empty);
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            ExportButton.IsEnabled = false;
        }
    }

    /// <summary>The base-game version's XML, for <see cref="Load"/> to diff against - or null if the
    /// file isn't modded, has no base-game original, or that original doesn't itself decode as .rml
    /// (<see cref="RmlDocument.TryDeserialize"/> failing rather than throwing here, same reasoning as
    /// <c>FcbXml.TryDecodeRmlShape</c>'s remarks - a mismatched original isn't exceptional).</summary>
    private string? TryRenderOriginalXml()
    {
        if (!_isModded)
        {
            return null;
        }

        try
        {
            byte[]? originalBytes = _readOriginal();
            return originalBytes is not null && RmlDocument.TryDeserialize(originalBytes, out XElement? originalRoot)
                ? originalRoot.ToString()
                : null;
        }
        catch
        {
            return null; // no usable base game version to diff against - fall through to plain view
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_xml is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export decoded XML as…",
            FileName = Path.GetFileNameWithoutExtension(_fileName) + ".xml",
            Filter = "XML file|*.xml",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _xml);
            StatusText.Text += $"\n\nExported to:\n{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't export: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
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
            XElement root = XDocument.Parse(File.ReadAllText(dialog.FileName)).Root
                ?? throw new InvalidDataException("Empty XML document.");
            byte[] rml = RmlDocument.Serialize(root);

            // Round-trips the freshly built file back through Deserialize as a validity check -
            // matches the same sanity check the Fcb/Xbt/Sbao handlers do before staging.
            RmlDocument.Deserialize(rml);

            _replaceContent(rml);
            Load(rml);
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
