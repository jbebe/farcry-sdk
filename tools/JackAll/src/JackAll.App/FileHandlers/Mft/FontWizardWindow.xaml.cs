using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Tools.Mft;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Mft;

/// <summary>
/// Draws a font from a TrueType or OpenType file: which font of the file, where on its design axes,
/// at what size and under what name, with sample text set from the pages it will ship with.
/// </summary>
public partial class FontWizardWindow : Window
{
    private readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private byte[]? _file;
    private string _autoName = "";
    private bool _fontStale;
    private int _latest;

    public FontWizardWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        SampleBox.Text = MftPreview.SampleText;
        _rebuild.Tick += async (_, _) =>
        {
            _rebuild.Stop();
            await Rebuild();
        };
    }

    /// <summary>The font as last previewed, staged once the window closes with true.</summary>
    public BuiltFont? Built { get; private set; }

    /// <summary>One design axis's slider.</summary>
    public sealed class AxisRow(FontAxis axis) : Observable
    {
        private double _value = axis.Value;

        public string Tag => axis.Tag;

        public string Label => axis.Name;

        public double Minimum => axis.Minimum;

        public double Maximum => axis.Maximum;

        public double Value
        {
            get => _value;
            set => Set(ref _value, Math.Round(value));
        }
    }

    private FontFace? Face => FacePicker.SelectedItem as FontFace;

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pick a font file",
            Filter = "Fonts|*.ttf;*.otf;*.ttc;*.otc|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string path = dialog.FileName;
            (byte[] file, IReadOnlyList<FontFace> faces) = await Task.Run(() =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                return (bytes, FontRaster.Faces(bytes));
            });
            _file = file;
            FileBox.Text = path;
            FacePicker.ItemsSource = faces;
            FacePicker.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read that font: {ex.Message}";
        }
    }

    private void Face_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Face is not { } face)
        {
            return;
        }
        AxisList.ItemsSource = face.Axes.Select(a => new AxisRow(a)).ToList();
        NameItself();
        Schedule(font: true);
    }

    private void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // The slider's first value arrives while the window is still being built.
        if (StageButton is null)
        {
            return;
        }
        NameItself();
        Schedule(font: true);
    }

    private void Setting_Changed(object sender, EventArgs e) => Schedule(font: true);

    private void Sample_Changed(object sender, EventArgs e) => Schedule(font: false);

    /// <summary>The family and size as the name, until the user writes one of their own.</summary>
    private void NameItself()
    {
        if (Face is not { } face || NameBox.Text != _autoName)
        {
            return;
        }
        _autoName = face.DefaultName((int)SizeSlider.Value);
        NameBox.Text = _autoName;
    }

    /// <summary>A preview after a pause; a new font first when <paramref name="font"/>, else only the sample set again.</summary>
    private void Schedule(bool font)
    {
        if (font)
        {
            _fontStale = true;
            StageButton.IsEnabled = false;
        }
        _rebuild.Stop();
        _rebuild.Start();
    }

    private async Task Rebuild()
    {
        if (_file is not { } file || Face is not { } face || NameBox.Text.Trim() is not { Length: > 0 } name)
        {
            return;
        }

        int generation = ++_latest;
        BuiltFont? current = _fontStale ? null : Built;
        int size = (int)SizeSlider.Value;
        Dictionary<string, float> axes = (AxisList.ItemsSource as IEnumerable<AxisRow> ?? []).ToDictionary(a => a.Tag, a => (float)a.Value);
        string source = Path.GetFileName(FileBox.Text);
        string sample = SampleBox.Text;
        try
        {
            (BuiltFont built, (byte[] Rgba, int Width, int Height) image) = await Task.Run(() =>
            {
                BuiltFont font = current ?? MftBuilder.Build(file, face.Index, size, name, source, axes);
                return (font, MftPreview.Render(font.Font, font.Pages, sample));
            });
            if (generation != _latest)
            {
                return;
            }

            Built = built;
            _fontStale = false;
            Preview.Source = XbtImage.ToBitmap(image.Rgba, image.Width, image.Height);
            StatusText.Text = $"{built.Font.CharacterCount} characters on {built.Pages.Count} page(s)."
                + (built.Missing.Count == 0 ? "" : $" The font lacks {built.Missing.Count} of the game's characters, which draw as nothing: "
                    + string.Concat(built.Missing.Take(16).Select(c => (char)c)) + (built.Missing.Count > 16 ? "…" : ""));
            StageButton.IsEnabled = true;
        }
        catch (Exception ex) when (generation == _latest)
        {
            StatusText.Text = $"Couldn't draw that font: {ex.Message}";
        }
    }

    private void Stage_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
