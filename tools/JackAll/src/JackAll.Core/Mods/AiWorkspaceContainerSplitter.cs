using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Ai;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// A brain workspace (`.ai.rml`) as an <see cref="IContainerSplitter"/>: one fragment per instance - a
/// brain, plan, scanner or task - of its <c>BlackBox.AI</c> source.
/// </summary>
/// <remarks>
/// A fragment is the instance's element as the source holds it, staged as
/// <c>&lt;last name segment&gt;.&lt;CRC-32 of the full name, decimal&gt;.xml</c> - the hash the compiled
/// repository keys tasks by. A plan's <c>Add</c> children are what it owns, so a mod adding a task also
/// stages the plan that adds it, and two mods adding to one plan merge. The build recompiles the
/// repository from the merged source. See docs/docs/file-formats/ai-rml.md.
/// </remarks>
public sealed class AiWorkspaceContainerSplitter : IContainerSplitter
{
    private const string Extension = ".ai.rml";
    private const string Indent = "  ";
    private static readonly HashSet<string> InstanceTags = ["Brain", "Plan", "Scanner", "Task"];

    public static AiWorkspaceContainerSplitter Instance { get; } = new();

    public static bool IsWorkspace(string fileName) => fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The id an instance is staged under, labelled by the last segment of its name.</summary>
    public static string IdOf(XElement instance)
    {
        string name = NameOf(instance);
        return FragmentId.Of(NumberOf(name), name[(name.LastIndexOf('/') + 1)..]);
    }

    public IContainerTree Open(byte[] container) => new Tree(AiWorkspaceFile.Read(container).Source);

    public string Canonicalize(string fragmentId, string fragmentXml) => FragmentXml.Render(XElement.Parse(fragmentXml), Indent);

    public (string Merged, IReadOnlyList<string> Conflicts) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => XmlListMerge.MergeTree(ancestor, ours, theirs, KeyOf);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XElement source = AiWorkspaceFile.Read(baseBytes).Source;
        var byNumber = new Dictionary<uint, XElement>();
        foreach (XElement instance in source.Elements())
        {
            byNumber[NumberOf(NameOf(instance))] = instance;
        }

        // By name, so a new plan is placed before the new tasks under it.
        foreach ((string id, XElement fragment) in fragmentXmlById
            .Select(kv => (kv.Key, XElement.Parse(kv.Value)))
            .OrderBy(f => NameOf(f.Item2), StringComparer.Ordinal))
        {
            uint number = NumberOf(NameOf(fragment));
            if (!InstanceTags.Contains(fragment.Name.LocalName) || FragmentId.NumberOf(id) != number)
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{fragment.Name.LocalName}> named '{NameOf(fragment)}'. "
                    + (InstanceTags.Contains(fragment.Name.LocalName) ? $"Name the file '{IdOf(fragment)}', or fix the name it holds." : ""));
            }

            if (byNumber.TryGetValue(number, out XElement? existing))
            {
                existing.ReplaceWith(fragment);
            }
            else
            {
                Place(source, fragment);
            }
            byNumber[number] = fragment;
        }

        return AiWorkspaceFile.Compile(source);
    }

    /// <summary>A new instance after the last one under its parent, as the editor writes them, or last.</summary>
    private static void Place(XElement source, XElement instance)
    {
        string name = NameOf(instance);
        string parent = name[..Math.Max(name.LastIndexOf('/'), 0)];
        XElement? after = source.Elements().LastOrDefault(e =>
            NameOf(e) == parent || NameOf(e).StartsWith(parent + "/", StringComparison.Ordinal));
        if (after is null)
        {
            source.Add(instance);
        }
        else
        {
            after.AddAfterSelf(instance);
        }
    }

    /// <summary>What a child is matched by in a merge: a connection by where it leads, a membership by
    /// the task, a selectable by its filter, and anything else by its name.</summary>
    private static string KeyOf(XElement child) => child.Name.LocalName switch
    {
        "Connection" => $"Connection|{(string?)child.Attribute("Target")}|{(string?)child.Attribute("TargetAnchor")}",
        "Add" => $"Add|{(string?)child.Attribute("Task")}",
        "Selectable" => $"Selectable|{(string?)child.Attribute("Filter")}",
        _ => (string?)child.Attribute("Name") is { } name ? $"{child.Name.LocalName}|{name}" : child.Name.LocalName,
    };

    private static string NameOf(XElement instance) => (string?)instance.Attribute("Name") ?? "";

    private static uint NumberOf(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private sealed class Tree(XElement source) : IContainerTree
    {
        private readonly Dictionary<string, XElement> _byId = source.Elements()
            .GroupBy(IdOf, FcbFragments.IdComparer)
            .ToDictionary(g => g.Key, g => g.Last(), FcbFragments.IdComparer);

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? instance) ? FragmentXml.Render(instance, Indent) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, kv.Value.ToString(SaveOptions.DisableFormatting).Length))];

        /// <summary>The source with every kept instance reduced to a marker in its place and the rest
        /// dropped, so an importer can tell a changed instance from a change around them.</summary>
        public string? Skeleton(Func<string, bool> keep)
            => new XElement(source.Name, source.Attributes(), source.Elements()
                .Select(IdOf)
                .Where(keep)
                .Select(id => new XElement("Instance", new XAttribute("id", id)))).ToString();
    }
}
