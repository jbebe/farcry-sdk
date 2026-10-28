using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Ai;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// A brain workspace (`.ai.rml`) as an <see cref="IContainerSplitter"/>: one fragment per instance - a
/// brain, plan, scanner or task - of its <c>BlackBox.AI</c> source, keyed by the task hash of its name.
/// See docs/docs/file-formats/ai-rml.md.
/// </summary>
public sealed class AiWorkspaceContainerSplitter : IContainerSplitter
{
    private const string Indent = "  ";
    private static readonly HashSet<string> InstanceTags = ["Brain", "Plan", "Scanner", "Task"];

    public static AiWorkspaceContainerSplitter Instance { get; } = new();

    public static bool IsWorkspace(string fileName) => fileName.EndsWith(AiWorkspaceFile.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The id an instance is staged under, labelled by the last segment of its name.</summary>
    public static string IdOf(XElement instance) => FragmentId.Of(NumberOf(instance), NameOf(instance));

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
        Dictionary<uint, XElement> byNumber = source.Elements().ToDictionary(NumberOf);

        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement fragment = XElement.Parse(xml);
            bool isInstance = InstanceTags.Contains(fragment.Name.LocalName);
            uint number = NumberOf(fragment);
            if (!isInstance || FragmentId.NumberOf(id) != number)
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{fragment.Name.LocalName}> named '{NameOf(fragment)}'. "
                    + (isInstance ? $"Name the file '{IdOf(fragment)}', or fix the name it holds." : ""));
            }

            if (byNumber.TryGetValue(number, out XElement? existing))
            {
                existing.ReplaceWith(fragment);
            }
            else
            {
                source.Add(fragment);
            }
            byNumber[number] = fragment;
        }

        return AiWorkspaceFile.Compile(source);
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

    private static uint NumberOf(XElement instance) => AiWorkspacePacker.Hash(NameOf(instance));

    private sealed class Tree(XElement source) : IContainerTree
    {
        private readonly Dictionary<string, XElement> _byId = source.Elements().ToDictionary(IdOf, FcbFragments.IdComparer);

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? instance) ? FragmentXml.Render(instance, Indent) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, kv.Value.ToString(SaveOptions.DisableFormatting).Length))];

        public string? Skeleton(Func<string, bool> keep)
            => new XElement(source.Name, source.Attributes(), source.Elements()
                .Select(IdOf)
                .Where(keep)
                .Select(id => new XElement("Instance", new XAttribute("id", id)))).ToString();
    }
}
