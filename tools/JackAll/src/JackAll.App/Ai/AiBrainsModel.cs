using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;
using JackAll.Core.Format;
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
    private bool _isSelected;

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

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value) && value && Children.Count == 1 && Children[0] == Placeholder && Node is { } node)
            {
                Children.Clear();
                HashSet<AiNode> selected = [.. node.Selectables.Select(s => s.Target).OfType<AiNode>()];
                foreach (AiSelectable selectable in node.Selectables.Where(s => s.Target is not null))
                {
                    Children.Add(new BrainTreeItem(selectable.Target!, selectable.Filter));
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
    private AiWorkspaceFile? _file;
    private AiBrainGraph? _graph;
    private AiBrainGraph? _vanilla;
    private string? _path;
    private AiNode? _selected;
    private string _search = "";
    private bool _isDirty;

    public IReadOnlyList<string> Brains { get; private set; } = [];

    public ObservableCollection<BrainTreeItem> Tree { get; } = [];

    public ObservableCollection<AiNode> SearchResults { get; } = [];

    public IReadOnlyList<BrainParameterRow> Parameters { get; private set; } = [];

    public IReadOnlyList<BrainLinkRow> Links { get; private set; } = [];

    public IReadOnlyList<BrainLinkRow> UsedBy { get; private set; } = [];

    public string? LoadedPath => _path;

    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public event Action? DirtyChanged;

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
                .Where(p => p.StartsWith(AiWorkspaceFile.Folder, StringComparison.OrdinalIgnoreCase)
                            && p.EndsWith(".ai.rml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => !p.EndsWith("mercbrain.ai.rml", StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase),
        ];
        OnPropertyChanged(nameof(Brains));
    }

    public async Task LoadAsync(string path)
    {
        byte[] bytes = vm.ReadByPath(path) ?? throw new InvalidDataException($"{path} could not be read");
        byte[]? original = vm.FindByHash(NameHash.Compute(path)) is { } row ? vm.ReadOriginal(row) : null;
        (AiWorkspaceFile file, AiBrainGraph graph, AiBrainGraph? vanilla) = await Task.Run(() =>
        {
            AiWorkspaceFile opened = AiWorkspaceFile.Read(bytes);
            return (opened, new AiBrainGraph(opened.Source),
                original is null ? null : new AiBrainGraph(AiWorkspaceFile.Read(original).Source));
        });

        _file = file;
        _graph = graph;
        _vanilla = vanilla;
        _path = path;
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

    public int NodeCount => _graph?.Nodes.Count() ?? 0;

    /// <summary>Recompiles the workspace and stages it.</summary>
    public async Task SaveAsync()
    {
        if (_file is null || _path is null || !IsDirty || vm.FindByHash(NameHash.Compute(_path)) is not { } row)
        {
            return;
        }
        XElement source = _file.Source;
        byte[] bytes = await Task.Run(() => new AiWorkspaceFile(AiWorkspacePacker.Pack(source), source).Write());
        vm.Replace(row, bytes);
        IsDirty = false;
        DirtyChanged?.Invoke();
    }

    private void ShowSelected()
    {
        AiNode? vanilla = _selected is null ? null : _vanilla?.Find(_selected.Name);
        Parameters = _selected is null ? [] : [.. Flatten(_selected.Element, vanilla?.Element, "")];
        Links = _selected is null
            ? []
            : [
                .. _selected.Selectables.Where(s => s.Target is not null)
                    .Select(s => new BrainLinkRow($"when {s.Filter} → runs {s.Target!.ShortName}", s.Target)),
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
            yield return new BrainParameterRow(parameter, prefix + name, (string?)vanilla?.Attribute("Value"), MarkDirty);
            foreach (BrainParameterRow nested in Flatten(parameter, vanilla, $"{prefix}{name}."))
            {
                yield return nested;
            }
        }
    }

    private void RunSearch()
    {
        SearchResults.Clear();
        string[] words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (_graph is null || words.Length == 0)
        {
            return;
        }
        foreach (AiNode node in _graph.Nodes
                     .Where(n => words.All(w => n.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                                || n.Class.Contains(w, StringComparison.OrdinalIgnoreCase)))
                     .Take(300))
        {
            SearchResults.Add(node);
        }
    }

    private void MarkDirty()
    {
        IsDirty = true;
        DirtyChanged?.Invoke();
    }

    private static string Describe(AiLink link) => link.Kind switch
    {
        "Anchor" => $"on {link.From.Replace("On", "", StringComparison.Ordinal).ToLowerInvariant()}",
        "Exit" => $"on {link.From.ToLowerInvariant()}",
        _ => $"on event {link.From}",
    };
}
