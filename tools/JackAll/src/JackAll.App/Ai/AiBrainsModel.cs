using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;
using JackAll.Core.Vfs;
using JackAll.Tools.Ai;

namespace JackAll.App.Ai;

/// <summary>
/// One row of the behaviour tree. A brain lists its filters first ("when combat"), each leading to the
/// plan it runs; a plan lists the tasks it adds. Children are built on first expand - the soldier brain
/// has 13,000 nodes.
/// </summary>
public sealed class BrainTreeItem : Observable
{
    private static readonly BrainTreeItem Placeholder = new();
    private bool _isExpanded;

    private BrainTreeItem()
    {
        Label = "";
        Detail = "";
    }

    public BrainTreeItem(AiNode node, string? filter = null)
    {
        Node = node;
        Label = filter is null ? node.ShortName : $"when {filter}";
        Detail = filter is null ? KindOf(node) : $"→ {node.ShortName}";
        if (node.Selectables.Count > 0 || node.Children.Count > 0)
        {
            Children.Add(Placeholder);
        }
    }

    public AiNode? Node { get; }

    public string Label { get; }

    public string Detail { get; }

    public ObservableCollection<BrainTreeItem> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value) && value && Children.Count == 1 && Children[0] == Placeholder && Node is { } node)
            {
                Children.Clear();
                HashSet<AiNode> selected = [.. node.Selectables.Select(s => s.Target)];
                foreach (AiSelectable selectable in node.Selectables)
                {
                    Children.Add(new BrainTreeItem(selectable.Target, selectable.Filter));
                }
                foreach (AiNode child in node.Children.Where(c => !selected.Contains(c)))
                {
                    Children.Add(new BrainTreeItem(child));
                }
            }
        }
    }

    public static string KindOf(AiNode node) => node.Kind switch
    {
        "Brain" => $"brain · {node.Class}",
        "Plan" => node.Class == "CPlan" ? "plan" : $"plan · {node.Class}",
        "Scanner" => $"sense · {node.Class}",
        _ => node.Class,
    };
}

/// <summary>A parameter of the selected node; editing it edits the workspace source.</summary>
public sealed class BrainParameterRow(XElement parameter, string label, string? vanilla, Action changed) : Observable
{
    public string Name { get; } = label;

    public string? Vanilla { get; } = vanilla;

    public string Value
    {
        get => (string?)parameter.Attribute("Value") ?? "";
        set
        {
            if (value != Value)
            {
                parameter.SetAttributeValue("Value", value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChanged));
                changed();
            }
        }
    }

    public bool IsChanged => Vanilla is not null && Vanilla != Value;
}

/// <summary>A wire out of the selected node, or a node that uses it; clicking one goes there.</summary>
public sealed record BrainLinkRow(string Text, AiNode Target);

/// <summary>The Brains view of the AI tab: browse a brain workspace and edit its task parameters.</summary>
public sealed class AiBrainsModel(MainViewModel vm) : Observable
{
    private VfsFile? _file;
    private AiBrainGraph? _graph;
    private Dictionary<string, XElement> _vanilla = [];
    private AiNode? _selected;
    private string _search = "";
    private bool _isDirty;

    public IReadOnlyList<string> Brains { get; private set; } = [];

    public ObservableCollection<BrainTreeItem> Tree { get; } = [];

    public IReadOnlyList<AiNode> SearchResults { get; private set; } = [];

    public IReadOnlyList<BrainParameterRow> Parameters { get; private set; } = [];

    public IReadOnlyList<BrainLinkRow> Links { get; private set; } = [];

    public IReadOnlyList<BrainLinkRow> UsedBy { get; private set; } = [];

    public string? LoadedPath => _file?.Path;

    public int NodeCount => _graph?.Count ?? 0;

    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public AiNode? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                ShowSelected();
            }
        }
    }

    public string Heading => _selected is { } n ? n.ShortName : "Pick a node in the tree";

    public string Summary => _selected is { } n
        ? $"{BrainTreeItem.KindOf(n)}{(n.Looping ? " · loops" : "")}\n{n.Name}\n{AiTaskHelp.Describe(n.Class)}"
        : "The tree starts at the brain. Each 'when …' row is a behaviour the C++ brain can pick; under it is the plan that runs, and under a plan the tasks it starts.";

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                RunSearch();
            }
        }
    }

    public void Initialize()
    {
        Brains =
        [
            .. vm.AllKnownPaths
                .Where(AiWorkspaceFile.IsWorkspace)
                .OrderBy(p => !p.EndsWith("mercbrain.ai.rml", StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase),
        ];
        OnPropertyChanged(nameof(Brains));
    }

    public async Task LoadAsync(string path)
    {
        VfsFile file = vm.FindByPath(path) ?? throw new InvalidDataException($"{path} is not in the game files");
        (AiBrainGraph graph, Dictionary<string, XElement> vanilla) = await Task.Run(() =>
        {
            byte[] bytes = vm.Read(file);
            byte[]? original = vm.ReadOriginal(file);
            XElement source = AiWorkspaceFile.Read(bytes).Source;
            XElement baseGame = original is null || original.AsSpan().SequenceEqual(bytes)
                ? new XElement(source)
                : AiWorkspaceFile.Read(original).Source;
            return (new AiBrainGraph(source),
                baseGame.Elements().DistinctBy(e => (string)e.Attribute("Name")!).ToDictionary(e => (string)e.Attribute("Name")!, StringComparer.Ordinal));
        });

        _file = file;
        _graph = graph;
        _vanilla = vanilla;
        Tree.Clear();
        foreach (AiNode root in graph.Roots)
        {
            Tree.Add(new BrainTreeItem(root) { IsExpanded = true });
        }
        Selected = null;
        IsDirty = false;
        RunSearch();
        OnPropertyChanged(nameof(LoadedPath));
    }

    /// <summary>Recompiles the workspace and stages it.</summary>
    public async Task SaveAsync()
    {
        if (_file is null || _graph is null || !IsDirty)
        {
            return;
        }
        XElement source = _graph.Source;
        vm.Replace(_file, await Task.Run(() => AiWorkspaceFile.Compile(source)));
        IsDirty = false;
    }

    private void ShowSelected()
    {
        Parameters = _selected is null ? [] : [.. Flatten(_selected.Element, _vanilla.GetValueOrDefault(_selected.Name), "")];
        Links = _selected is null
            ? []
            : [
                .. _selected.Selectables.Select(s => new BrainLinkRow($"when {s.Filter} → runs {s.Target.ShortName}", s.Target)),
                .. _selected.Links.Select(l => new BrainLinkRow($"{Describe(l)} → {l.TargetAnchor.ToLowerInvariant()} {l.Target.ShortName}", l.Target)),
                .. _selected.Children.Select(c => new BrainLinkRow($"adds {c.ShortName} ({c.Class})", c)),
            ];
        UsedBy = _selected is null ? [] : [.. _selected.Parents.Select(p => new BrainLinkRow($"{p.ShortName} ({p.Class})", p))];
        OnPropertyChanged(nameof(Parameters));
        OnPropertyChanged(nameof(Links));
        OnPropertyChanged(nameof(UsedBy));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Summary));
    }

    private IEnumerable<BrainParameterRow> Flatten(XElement owner, XElement? vanillaOwner, string prefix)
    {
        foreach (XElement parameter in owner.Elements("Parameter"))
        {
            string name = (string)parameter.Attribute("Name")!;
            XElement? vanilla = vanillaOwner?.Elements("Parameter").FirstOrDefault(p => (string?)p.Attribute("Name") == name);
            yield return new BrainParameterRow(parameter, prefix + name, (string?)vanilla?.Attribute("Value"), () => IsDirty = true);
            foreach (BrainParameterRow nested in Flatten(parameter, vanilla, $"{prefix}{name}."))
            {
                yield return nested;
            }
        }
    }

    private void RunSearch()
    {
        string[] words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        SearchResults = _graph is null || words.Length == 0
            ? []
            : [.. _graph.Nodes
                .Where(n => words.All(w => n.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                           || n.Class.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .Take(300)];
        OnPropertyChanged(nameof(SearchResults));
    }

    private static string Describe(AiLink link) => link.Kind switch
    {
        "Anchor" => $"on {link.From.Replace("On", "", StringComparison.Ordinal).ToLowerInvariant()}",
        "Exit" => $"on {link.From.ToLowerInvariant()}",
        _ => $"on event {link.From}",
    };
}
