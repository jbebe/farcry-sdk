using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackAll.App.Picker;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using JackAll.Tools.Xbm;

namespace JackAll.App.FileHandlers.Xbm;

/// <summary>
/// The `.xbm` editor: a material's name, shader and every property the shader takes, grouped as the
/// game's own descriptors group them, with the mistakes the engine makes silently called out.
/// </summary>
/// <remarks>
/// Each committed edit stages the material straight away; the host keeps this panel over the file it
/// staged. The shader schema comes from the merged filesystem, so without a game loaded the material's
/// own properties are still editable, just unchecked.
/// </remarks>
public partial class XbmFileHandler : UserControl
{
    private readonly Action<byte[]> _replaceContent;
    private readonly Action<VfsFile> _navigateTo;
    private readonly ShaderSchema? _schema;
    private readonly XbmFile? _material;

    public XbmFileHandler(byte[] content, Action<byte[]> replaceContent, Action<VfsFile> navigateTo)
    {
        InitializeComponent();
        _replaceContent = replaceContent;
        _navigateTo = navigateTo;
        _schema = FilePicker.FileOf(NameHash.Compute(ShaderSchema.GamePath)) is { } descriptors
            ? ShaderSchema.Parse(FilePicker.Read(descriptors))
            : null;

        try
        {
            _material = XbmFile.Parse(content);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            IsEnabled = false;
            return;
        }

        NameBox.Text = _material.Name;
        ShaderPicker.ItemsSource = _schema?.Names.Order(StringComparer.Ordinal).ToList() ?? [_material.Shader];
        ShaderPicker.SelectedItem = _material.Shader;
        if (_schema is null)
        {
            StatusText.Text = "No game is loaded, so nothing checks these values against the shader.";
        }
        Refresh();
    }

    private ShaderDescriptor? Shader => _schema?.Find(_material!.Shader);

    /// <summary>Rebuilds the rows from the material, which every edit has already changed.</summary>
    private void Refresh()
    {
        XbmFile material = _material!;
        ShaderDescriptor? shader = Shader;
        List<MaterialProblem> problems = _schema is null
            ? []
            : MaterialLint.Check(material, _schema, path => FilePicker.FileOf(NameHash.Compute(path)) is not null);

        List<MaterialGroup> groups = [];
        if (shader is not null)
        {
            bool all = ShowAllBox.IsChecked == true;
            foreach (IGrouping<string, ShaderParameter> group in shader.Parameters.GroupBy(p => p.Group))
            {
                List<MaterialRow> rows = [.. group
                    .Where(p => all || material.Find(p.Name) is not null)
                    .Select(p => Row(p.Name, p, problems))];
                if (rows.Count > 0)
                {
                    groups.Add(new MaterialGroup(group.Key, rows));
                }
            }
        }
        List<MaterialRow> stray = [.. material.Entries.DistinctBy(e => e.Key)
            .Where(e => shader?.Find(e.Key) is null)
            .Select(e => Row(e.Key, null, problems))];
        if (stray.Count > 0)
        {
            groups.Add(new MaterialGroup(shader is null ? "Properties" : $"Not taken by {shader.Name}", stray));
        }
        GroupsList.ItemsSource = groups;

        LinkBox.Visibility = shader?.Parameters.Any(p => shader.CleanTwin(p.Name) is not null) == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        string[] general = [.. problems.Where(p => p.Key is null).Select(p => p.Message)];
        ProblemsText.Text = string.Join(Environment.NewLine, general);
        ProblemsPanel.Visibility = general.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private MaterialRow Row(string key, ShaderParameter? parameter, List<MaterialProblem> problems)
    {
        XbmEntry? entry = _material!.Find(key);
        return new MaterialRow(key, entry?.Text ?? string.Empty, Edit)
        {
            IsTexture = (entry?.Section ?? parameter!.Section) == XbmSection.Texture,
            Hint = entry is not null ? null
                : parameter?.Default is { } fallback ? $"Unset: the shader's default is {fallback}." : "Unset.",
            Swatch = parameter is { IsColor: true } && entry is { Floats.Length: >= 3 } ? Swatch(entry.Floats) : null,
            Problem = problems.FirstOrDefault(p => p.Key == key)?.Message,
        };
    }

    /// <summary>The colour as it would show at full light; a channel over 1 shows as 1.</summary>
    private static SolidColorBrush Swatch(float[] rgb)
    {
        static byte Channel(float value) => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
        var brush = new SolidColorBrush(Color.FromRgb(Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2])));
        brush.Freeze();
        return brush;
    }

    /// <summary>Sets a property from its text, or removes it when the text is cleared.</summary>
    private void Edit(string key, string text)
    {
        XbmFile material = _material!;
        if (text.Trim() == (material.Find(key)?.Text ?? string.Empty))
        {
            return;
        }

        try
        {
            if (text.Trim().Length == 0)
            {
                material.Remove(key);
            }
            else if (Shader is { } shader)
            {
                shader.Set(material, key, text, LinkBox.IsChecked == true);
            }
            else
            {
                XbmEntry current = material.Find(key)!;
                material.Set(XbmEntry.Parse(key, current.Section, current.Floats.Length, text));
            }
            Stage();
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException)
        {
            StatusText.Text = ex.Message;
        }
        // After the input that caused it has finished, so the box being committed is not torn down under it.
        Dispatcher.BeginInvoke(Refresh);
    }

    private void Stage()
    {
        byte[] bytes = _material!.Write();
        // A read-back check: a material that fails to parse is never staged.
        XbmFile.Parse(bytes);
        _replaceContent(bytes);
        StatusText.Text = "Staged in your workspace.";
    }

    private void Name_LostFocus(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (_material is null || name.Length == 0 || name == _material.Name)
        {
            return;
        }
        _material.Name = name;
        Stage();
    }

    private void Shader_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_material is null || ShaderPicker.SelectedItem is not string shader || shader == _material.Shader)
        {
            return;
        }
        _material.Shader = shader;
        Stage();
        Refresh();
    }

    private void Options_Click(object sender, RoutedEventArgs e) => Refresh();

    private void PickTexture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MaterialRow row } button)
        {
            return;
        }
        var request = new FilePickerRequest("Pick a texture", Extension: "xbt",
            InitialPath: row.Text.Length > 0 ? row.Text : null, NeedsRealPath: true);
        if (FilePicker.Show(button, request) is { } file)
        {
            row.Text = file.Path;
        }
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MaterialRow { Texture: { } file } })
        {
            _navigateTo(file);
        }
    }
}
