using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JackAll.App.FileHandlers.Xbt;
using JackAll.App.Picker;
using JackAll.Core.Vfs;
using JackAll.Tools.Xbm;

namespace JackAll.App.FileHandlers.Xbm;

/// <summary>
/// The `.xbm` editor: every property the shader takes, grouped as the game's descriptors group them,
/// with what the engine would silently ignore called out. Edits stage shortly after the last one.
/// </summary>
public partial class XbmFileHandler : UserControl
{
    private readonly XbmFile _material;
    private readonly Action<byte[]> _replaceContent;
    private readonly Action<VfsFile> _navigateTo;
    private readonly DispatcherTimer _stageTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly ConcurrentDictionary<string, ImageSource> _thumbnails = new(StringComparer.OrdinalIgnoreCase);

    private XbmFileHandler(XbmFile material, Action<byte[]> replaceContent, Action<VfsFile> navigateTo)
    {
        InitializeComponent();
        _material = material;
        _replaceContent = replaceContent;
        _navigateTo = navigateTo;
        _stageTimer.Tick += (_, _) => Stage();
        Unloaded += (_, _) =>
        {
            if (_stageTimer.IsEnabled)
            {
                Stage();
            }
        };

        NameBox.Text = material.Name;
        ShaderPicker.ItemsSource = FilePicker.Shaders?.Names.ToList() ?? [material.Shader];
        ShaderPicker.SelectedItem = material.Shader;
        if (FilePicker.Shaders is null)
        {
            StatusText.Text = "No game is loaded, so nothing checks these values against the shader.";
        }
        Refresh();
    }

    /// <summary>The editor, or the reason the file could not be read.</summary>
    public static UserControl For(byte[] content, Action<byte[]> replaceContent, Action<VfsFile> navigateTo)
    {
        try
        {
            return new XbmFileHandler(XbmFile.Parse(content), replaceContent, navigateTo);
        }
        catch (Exception ex)
        {
            return new UserControl
            {
                Content = new TextBlock { Text = $"Couldn't read this file: {ex.Message}", Margin = new Thickness(16) },
            };
        }
    }

    private ShaderDescriptor? Shader => FilePicker.Shaders?.Find(_material.Shader);

    /// <summary>Rebuilds the rows from the material, which every edit has already changed.</summary>
    private void Refresh()
    {
        ShaderDescriptor? shader = Shader;
        List<MaterialProblem> problems = FilePicker.Shaders is { } schema
            ? MaterialLint.Check(_material, schema, path => FilePicker.FileOfPath(path) is not null)
            : [];

        List<MaterialGroup> groups = [];
        if (shader is not null)
        {
            bool all = ShowAllBox.IsChecked == true;
            foreach (IGrouping<string, ShaderParameter> group in shader.Parameters.GroupBy(p => p.Group))
            {
                List<MaterialRow> rows = [.. group
                    .Where(p => all || _material.Find(p.Name) is not null)
                    .Select(p => Row(p.Name, p, problems))];
                if (rows.Count > 0)
                {
                    groups.Add(new MaterialGroup(group.Key, rows));
                }
            }
        }
        List<MaterialRow> stray = [.. _material.Entries.DistinctBy(e => e.Key)
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
        XbmEntry? entry = _material.Find(key);
        return new MaterialRow(key, entry?.Text ?? string.Empty, Edit, Thumbnail)
        {
            IsTexture = (entry?.Section ?? parameter!.Section) == XbmSection.Texture,
            Hint = entry is not null ? null
                : parameter?.Default is { } fallback ? $"Unset: the shader's default is {fallback}." : "Unset.",
            Swatch = parameter is { IsColor: true } && entry is { Floats.Length: >= 3 } ? ColourSwatch.Of(entry.Floats) : null,
            Problem = problems.FirstOrDefault(p => p.Key == key)?.Message,
        };
    }

    /// <summary>Kept across rebuilds, since every reindex makes the file rows new; read off the UI thread.</summary>
    private ImageSource? Thumbnail(string path)
    {
        if (_thumbnails.TryGetValue(path, out ImageSource? cached))
        {
            return cached;
        }
        ImageSource? image = FilePicker.FileOfPath(path) is { Type.Extension: "xbt" } file
            ? XbtImage.Thumbnail(file, FilePicker.Read)
            : null;
        return image is null ? null : _thumbnails.GetOrAdd(path, image);
    }

    /// <summary>Sets a property from its text, or removes it when the text is cleared.</summary>
    private void Edit(string key, string text)
    {
        text = text.Trim();
        if (text == (_material.Find(key)?.Text ?? string.Empty))
        {
            return;
        }

        try
        {
            if (text.Length == 0)
            {
                _material.Remove(key);
            }
            else
            {
                MaterialEdit.Set(_material, Shader, key, text, LinkBox.IsChecked == true);
            }
            ScheduleStage();
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException)
        {
            StatusText.Text = ex.Message;
        }
        // After the input that caused it has finished, so the box being committed is not torn down under it.
        Dispatcher.BeginInvoke(Refresh);
    }

    private void ScheduleStage()
    {
        StatusText.Text = "Staging…";
        _stageTimer.Stop();
        _stageTimer.Start();
    }

    private void Stage()
    {
        _stageTimer.Stop();
        byte[] bytes = _material.Write();
        // A read-back check: a material that fails to parse is never staged.
        XbmFile.Parse(bytes);
        _replaceContent(bytes);
        StatusText.Text = "Staged in your workspace.";
    }

    private void Name_LostFocus(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length > 0 && name != _material.Name)
        {
            _material.Name = name;
            ScheduleStage();
        }
    }

    private void Shader_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShaderPicker.SelectedItem is string shader && shader != _material.Shader)
        {
            _material.Shader = shader;
            ScheduleStage();
            Refresh();
        }
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
