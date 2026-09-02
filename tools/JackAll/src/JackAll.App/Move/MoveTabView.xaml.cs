using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackAll.Core;
using JackAll.Core.Naming;
using JackAll.Tools.Move;
using Microsoft.Win32;

namespace JackAll.App.Move;

/// <summary>
/// The Move tab: the animation graph the engine picks clips with, browsable as the ownership tree
/// it reads back as. Read-only - editing goes through <c>jackall-cli move decode/encode</c>.
/// </summary>
/// <remarks>Criteria and channels are labelled from the named twin's channel table when the export
/// has one, which is the difference between "17 == 42" and "EquippedWeapon == SawedOffShotgun".
/// The loadable graphs carry no names at all.</remarks>
public partial class MoveTabView : UserControl
{
    private MainViewModel? _vm;
    private MoveFile? _file;
    private MoveTreeNode? _root;
    private IReadOnlyList<MoveChannel>? _channels;

    public MoveTabView() => InitializeComponent();

    /// <summary>Called by MainWindow once the VFS is loaded and its paths become discoverable.</summary>
    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        List<string> graphs = [.. Discover(vm.AllKnownPaths)];

        GraphPicker.ItemsSource = graphs;
        GraphPicker.SelectedIndex = 0;
        GraphPicker.IsEnabled = graphs.Count > 0;
        LoadButton.IsEnabled = graphs.Count > 0;
        StatusText.Text = graphs.Count > 0
            ? $"{graphs.Count} graphs - pick one and Load"
            : "No MOVE graphs found";
    }

    /// <summary>
    /// The loadable graphs. A named twin is the authoring form: <c>CreateFromStream</c> rejects it
    /// and only ~90% of it is decoded, so listing it would offer a file that cannot be opened.
    /// </summary>
    private static IEnumerable<string> Discover(IEnumerable<string> paths) =>
        paths.Where(p =>
                p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                && p.Replace('/', '\\').Contains("\\move\\", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileNameWithoutExtension(p)
                    .EndsWith("named", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>The named twin beside a graph, which is the only place channel names survive.</summary>
    private byte[]? ReadNamedTwin(string path)
    {
        string twin = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            Path.GetFileNameWithoutExtension(path) + "named.bin");
        return _vm?.ReadByPath(twin);
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || GraphPicker.SelectedItem is not string path)
        {
            return;
        }

        LoadButton.IsEnabled = false;
        StatusText.Text = "Loading…";
        try
        {
            byte[] data = _vm.ReadByPath(path)
                ?? throw new MoveFormatException($"{path} could not be read");
            byte[]? namedData = ReadNamedTwin(path);

            (MoveFile file, MoveTreeNode root, IReadOnlyList<MoveChannel>? channels) =
                await Task.Run(() =>
                {
                    MoveFile parsed = MoveCodec.Load(data);
                    IReadOnlyList<MoveChannel>? table = namedData is null
                        ? null
                        : MoveCodec.ChannelTable(namedData);
                    return (parsed, MoveTreeNode.Build(parsed, table), table);
                });

            _file = file;
            _root = root;
            _channels = channels;
            _root.IsExpanded = true;
            ObjectTree.ItemsSource = new[] { _root };
            FieldGrid.ItemsSource = null;
            DetailHeader.Text = "Select an object to see its fields";
            ExportButton.IsEnabled = true;

            string names = channels is null ? "no channel names beside it" : $"{channels.Count} channels named";
            StatusText.Text =
                $"{file.Objects.Count:N0} objects, {file.StateMachine?.Field("nbState") ?? 0} states - {names}";
        }
        catch (Exception ex) when (ex is MoveFormatException or IOException)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_file is null)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "XML documents (*.xml)|*.xml",
            FileName = Path.GetFileNameWithoutExtension(GraphPicker.SelectedItem as string ?? "movemgr") + ".xml",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        NameDatabase names = BundledAssets.LoadNames();
        MoveLabels labels = new(
            _channels, hash => names.TryResolve(hash, out string path) ? path : null);
        File.WriteAllText(dialog.FileName, MoveXml.ToXml(_file, labels));
        StatusText.Text = $"Wrote {dialog.FileName}";
    }

    private void ObjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not MoveTreeNode node)
        {
            return;
        }

        DetailHeader.Text = $"{node.Target.ClassName} #{node.Target.Index}";
        FieldGrid.ItemsSource = MoveTreeNode.Fields(node.Target, _channels);
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_root is not null)
        {
            MoveTreeNode.Filter(_root, SearchBox.Text.Trim());
        }
    }
}
