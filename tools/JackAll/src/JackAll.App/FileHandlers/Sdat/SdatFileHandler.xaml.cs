using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JackAll.Core.Format;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Sdat;

/// <summary>
/// The file handler for .sdat world-sector terrain chunks — a read-only viewer, not an editor (the
/// game's own Map Editor is the actual tool for shaping terrain; the generated `.sdat` files are
/// disposable and get regenerated from the editor's own working copy on every save). Renders the
/// sector's 65x65 height grid as grayscale, white its highest point,
/// black its lowest — normalized per-sector, not to the raw 0-65535 range, since real terrain heights
/// only ever occupy a small slice of that range and a non-normalized render would be an almost-flat,
/// unreadable gray square.
/// </summary>
public partial class SdatFileHandler : UserControl
{
    private readonly string _fileName;
    private WriteableBitmap? _bitmap;

    public SdatFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        _fileName = fileName;
        Load(content);
    }

    private void Load(byte[] content)
    {
        try
        {
            SdatSector sector = SdatSectorFile.Decode(content);
            (ushort min, ushort max) = MinMax(sector.Grid);
            _bitmap = ToGrayscaleBitmap(sector.Grid, min, max);
            Preview.Source = _bitmap;

            StatusText.Text =
                $"{_fileName}\n\n" +
                $"Sector {sector.SectorId} at ({sector.X:0.##}, {sector.Y:0.##})\n" +
                $"{SdatSectorFile.GridSize}x{SdatSectorFile.GridSize} height samples ({SdatSectorFile.GridSize - 1}x{SdatSectorFile.GridSize - 1} quads)\n" +
                $"Lowest: {min * SdatSectorFile.MetersPerUnit:0.##} m   Highest: {max * SdatSectorFile.MetersPerUnit:0.##} m\n\n" +
                "White = this sector's highest point, black = its lowest (scaled to this sector, not " +
                "an absolute height).";
            ExportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _bitmap = null;
            Preview.Source = null;
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            ExportButton.IsEnabled = false;
        }
    }

    private static (ushort Min, ushort Max) MinMax(SdatGridCell[,] grid)
    {
        ushort min = ushort.MaxValue;
        ushort max = ushort.MinValue;
        foreach (SdatGridCell cell in grid)
        {
            if (cell.RawHeight < min) min = cell.RawHeight;
            if (cell.RawHeight > max) max = cell.RawHeight;
        }
        return (min, max);
    }

    private static WriteableBitmap ToGrayscaleBitmap(SdatGridCell[,] grid, ushort min, ushort max)
    {
        int size = SdatSectorFile.GridSize;
        int range = max - min;

        var buffer = new byte[size * size * 4]; // Bgra32
        int i = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // A perfectly flat sector (range == 0) reads as mid-gray rather than dividing by zero.
                byte level = range == 0 ? (byte)128 : (byte)((grid[y, x].RawHeight - min) * 255 / range);
                buffer[i++] = level;
                buffer[i++] = level;
                buffer[i++] = level;
                buffer[i++] = 255;
            }
        }

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), buffer, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_bitmap is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export heightmap as PNG",
            FileName = Path.GetFileNameWithoutExtension(_fileName) + ".png",
            Filter = "PNG file|*.png",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_bitmap));
            using FileStream stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            StatusText.Text += $"\n\nExported:\n{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't export: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
