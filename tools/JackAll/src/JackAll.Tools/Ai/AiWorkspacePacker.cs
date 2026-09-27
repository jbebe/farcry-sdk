using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;

namespace JackAll.Tools.Ai;

/// <summary>
/// Compiles <c>BlackBox.AI</c> source into the repository <c>CTaskRepository</c> loads - a port of
/// <c>CTaskPackingRepository::CreatePackedRepository</c>. Matches the shipped brains byte for byte
/// except where each task's connection slice sits in the shared buffer, which the engine reads by offset.
/// </summary>
public static class AiWorkspacePacker
{
    public static byte[] Pack(XElement source)
    {
        var repo = new AiPackedRepository();
        var blobIndex = new Dictionary<(uint, string), ushort>();
        var tasks = new Dictionary<uint, TaskData>();
        var instances = source.Elements().ToList();

        foreach (XElement instance in instances)
        {
            uint cls = Hash((string?)instance.Attribute("Class") ?? "");
            byte[] rml = RmlDocument.Serialize(ParametersOf(instance));
            var key = (cls, Convert.ToBase64String(rml));
            if (!blobIndex.TryGetValue(key, out ushort blob))
            {
                blob = (ushort)repo.Blobs.Count;
                blobIndex.Add(key, blob);
                repo.Blobs.Add(new AiParameterBlob(cls, rml));
            }
            string name = (string)instance.Attribute("Name")!;
            tasks.TryAdd(Hash(name), new TaskData(name, blob));
        }

        List<TaskData> sorted = [.. tasks.Values.OrderBy(t => t.Hash)];
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Index = (ushort)i;
        }

        foreach (XElement instance in instances)
        {
            TaskData task = tasks[Hash((string)instance.Attribute("Name")!)];
            foreach (XElement child in instance.Elements())
            {
                AddConnections(repo, tasks, task, child);
            }
        }

        var buffer = new List<byte>();
        foreach (TaskData t in sorted)
        {
            repo.Tasks.Add(new AiPackedTask(t.Hash, t.Name, t.Blob, (uint)buffer.Count, (ushort)t.Connections.Count));
            buffer.AddRange(t.Connections);
        }
        repo.Connections = [.. buffer];
        return repo.Write();
    }

    /// <summary>The <c>Parameters</c> node <c>LoadInstance</c> builds: flags as attributes, then one
    /// attribute per parameter and a child element for each parameter that has sub-parameters.</summary>
    private static XElement ParametersOf(XElement instance)
    {
        var node = new XElement("Parameters", new XAttribute("Name", " "));
        foreach (string flag in (string[])["Looping", "Independent"])
        {
            if (instance.Attribute(flag) is { } value)
            {
                node.SetAttributeValue(flag, value.Value);
            }
        }
        AddParameters(node, instance);
        return node;
    }

    private static void AddParameters(XElement node, XElement owner)
    {
        foreach (XElement parameter in owner.Elements("Parameter"))
        {
            string name = RmlDocument.EncodeName((string)parameter.Attribute("Name")!);
            node.SetAttributeValue(name, (string?)parameter.Attribute("Value") ?? "");
            if (parameter.HasElements)
            {
                var child = new XElement(name);
                node.Add(child);
                AddParameters(child, parameter);
            }
        }
    }

    private static void AddConnections(AiPackedRepository repo, Dictionary<uint, TaskData> tasks, TaskData task, XElement child)
    {
        switch (child.Name.LocalName)
        {
            case "Selectable" when tasks.GetValueOrDefault(Hash((string?)child.Attribute("Task") ?? "")) is { } target:
                task.Connections.Add((byte)AiConnectionKind.Selectable);
                WriteU16(task.Connections, target.Index);
                WriteU16(task.Connections, AnchorIndex(repo, (string)child.Attribute("Filter")!));
                break;

            case "Anchor" or "Exit" or "Event" when child.HasElements:
                var kind = child.Name.LocalName switch
                {
                    "Anchor" => AiConnectionKind.Anchor,
                    "Exit" => AiConnectionKind.Exit,
                    _ => AiConnectionKind.Event,
                };
                var connections = child.Elements().ToList();
                task.Connections.Add((byte)kind);
                WriteU16(task.Connections, AnchorIndex(repo, (string)child.Attribute("Name")!));
                WriteU16(task.Connections, (ushort)connections.Count);
                foreach (XElement connection in connections)
                {
                    string target = (string)connection.Attribute("Target")!;
                    WriteU16(task.Connections, tasks[Hash(target)].Index);
                    WriteU16(task.Connections, AnchorIndex(repo, (string)connection.Attribute("TargetAnchor")!));
                    // 1 when the target lives inside this task, 2 otherwise.
                    task.Connections.Add((byte)(target.StartsWith(task.Name, StringComparison.Ordinal) ? 1 : 2));
                }
                break;

            case "Add" when tasks.GetValueOrDefault(Hash((string)child.Attribute("Task")!)) is { } added:
                added.Connections.InsertRange(0, [(byte)AiConnectionKind.Owner, (byte)task.Index, (byte)(task.Index >> 8)]);
                break;
        }
    }

    private static ushort AnchorIndex(AiPackedRepository repo, string name)
    {
        uint hash = Hash(name);
        int index = repo.Anchors.IndexOf(hash);
        if (index < 0)
        {
            index = repo.Anchors.Count;
            repo.Anchors.Add(hash);
        }
        return (ushort)index;
    }

    private static void WriteU16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private sealed class TaskData(string name, ushort blob)
    {
        public string Name { get; } = name;

        public uint Hash { get; } = AiWorkspacePacker.Hash(name);

        public ushort Blob { get; } = blob;

        public ushort Index { get; set; }

        public List<byte> Connections { get; } = [];
    }
}
