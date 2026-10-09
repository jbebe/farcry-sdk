using JackAll.Tools.Xbt;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows;

namespace JackAll.App.FileHandlers.Xbt;

/// <summary>
/// The file handler for .xbt textures. Splits the file into its DDS payload and a companion header
/// XML on load, previews the DDS, and offers both export (DDS + XML pair) and import (rebuilding an
/// .xbt from a replacement DDS, under its header XML or a fresh header, staged into the workspace).
/// </summary>
public partial class XbtFileHandler : UserControl
{
    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private byte[]? _header;
    private byte[]? _dds;

    public XbtFileHandler(string fileName, byte[] content, Action<byte[]> replaceContent)
    {
        InitializeComponent();
        _fileName = fileName;
        _replaceContent = replaceContent;
        Load(content);
    }

    private void Load(byte[] content)
    {
        try
        {
            (byte[] header, byte[] dds) = XbtTexture.Split(content);
            _header = header;
            _dds = dds;

            StatusText.Text =
                $"{_fileName}\n\n" +
                $"Header: {header.Length:N0} bytes\n" +
                $"DDS payload: {dds.Length:N0} bytes\n\n" +
                "Ready to export.";
            ExportButton.IsEnabled = true;

            Preview.Source = XbtImage.TryDecode(content, out string? previewError);
            if (previewError is not null)
            {
                StatusText.Text += $"\n\nNo preview available: {previewError}";
            }
        }
        catch (Exception ex)
        {
            _header = null;
            _dds = null;
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            ExportButton.IsEnabled = false;
            Preview.Source = null;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_dds is null || _header is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export DDS",
            FileName = Path.GetFileNameWithoutExtension(_fileName) + ".dds",
            Filter = "DDS file|*.dds",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        string ddsPath = dialog.FileName;
        string xmlPath = Path.ChangeExtension(ddsPath, ".xml");

        try
        {
            File.WriteAllBytes(ddsPath, _dds);
            File.WriteAllText(xmlPath, XbtTexture.ToXml(_header));
            StatusText.Text += $"\n\nExported:\n{ddsPath}\n{xmlPath}";
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
            Title = "Import - select the replacement .dds, and its .xml header if it has one",
            Filter = "DDS + XML|*.dds;*.xml",
            Multiselect = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        string? ddsPath = dialog.FileNames.FirstOrDefault(
            p => Path.GetExtension(p).Equals(".dds", StringComparison.OrdinalIgnoreCase));
        string? xmlPath = dialog.FileNames.FirstOrDefault(
            p => Path.GetExtension(p).Equals(".xml", StringComparison.OrdinalIgnoreCase));

        if (ddsPath is null || dialog.FileNames.Length != (xmlPath is null ? 1 : 2))
        {
            MessageBox.Show(Window.GetWindow(this),
                "Select one .dds file, and its matching .xml header file if it has one.",
                "JackAll", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            byte[] dds = File.ReadAllBytes(ddsPath);
            // A lone .dds keeps this texture's flags but names no companion, so it carries the whole chain.
            byte[] header = xmlPath is null
                ? XbtTexture.NewHeader(_header is null ? XbtTexture.DefaultFlags : XbtTexture.Flags(_header))
                : XbtTexture.HeaderFromXml(File.ReadAllText(xmlPath));
            byte[] combined = XbtTexture.Combine(header, dds);

            // Round-trips the freshly built file back through Split as a validity check — this
            // throws the same way a corrupt XBT would if HeaderSize doesn't land on a DDS payload.
            XbtTexture.Split(combined);

            _replaceContent(combined);
            Load(combined);
            StatusText.Text += $"\n\nImported from:\n{ddsPath}\n{xmlPath ?? "a new header"}\n\nStaged in your workspace.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't import: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
