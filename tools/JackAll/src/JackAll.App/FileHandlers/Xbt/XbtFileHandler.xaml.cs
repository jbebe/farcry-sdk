using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using JackAll.Core.Format;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Xbt;

/// <summary>
/// The file handler for .xbt textures. Splits the file into its DDS payload and a companion header
/// XML on load, previews the DDS, and offers both export (DDS + XML pair) and import (rebuilding an
/// .xbt from a replacement DDS + its header XML, staged into the workspace).
/// </summary>
public partial class XbtFileHandler : UserControl
{
    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private byte[]? _dds;
    private string? _xml;

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
            _dds = dds;
            _xml = XbtTexture.ToXml(header);

            StatusText.Text =
                $"{_fileName}\n\n" +
                $"Header: {header.Length:N0} bytes\n" +
                $"DDS payload: {dds.Length:N0} bytes\n\n" +
                "Ready to export.";
            ExportButton.IsEnabled = true;

            ShowPreview(dds);
        }
        catch (Exception ex)
        {
            _dds = null;
            _xml = null;
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            ExportButton.IsEnabled = false;
            Preview.Source = null;
        }
    }

    private void ShowPreview(byte[] dds)
    {
        try
        {
            var decoder = new BcDecoder();
            using var stream = new MemoryStream(dds);
            Preview.Source = ToBitmap(decoder.Decode2D(stream));
        }
        catch (Exception ex)
        {
            Preview.Source = null;
            StatusText.Text += $"\n\nNo preview available: {ex.Message}";
        }
    }

    private static WriteableBitmap ToBitmap(Memory2D<ColorRgba32> pixels)
    {
        int width = pixels.Width;
        int height = pixels.Height;
        ColorRgba32[,] rows = pixels.ToArray();

        byte[] buffer = new byte[width * height * 4];
        int i = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ColorRgba32 c = rows[y, x];
                buffer[i++] = c.b;
                buffer[i++] = c.g;
                buffer[i++] = c.r;
                buffer[i++] = c.a;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_dds is null || _xml is null)
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
            File.WriteAllText(xmlPath, _xml);
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
            Title = "Import - select the replacement .dds and its .xml header",
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

        if (dialog.FileNames.Length != 2 || ddsPath is null || xmlPath is null)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Select exactly one .dds file and its matching .xml header file.",
                "JackAll", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            byte[] dds = File.ReadAllBytes(ddsPath);
            byte[] header = XbtTexture.HeaderFromXml(File.ReadAllText(xmlPath));
            byte[] combined = XbtTexture.Combine(header, dds);

            // Round-trips the freshly built file back through Split as a validity check — this
            // throws the same way a corrupt XBT would if HeaderSize doesn't land on a DDS payload.
            XbtTexture.Split(combined);

            _replaceContent(combined);
            Load(combined);
            StatusText.Text += $"\n\nImported from:\n{ddsPath}\n{xmlPath}\n\nStaged in your workspace.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't import: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
