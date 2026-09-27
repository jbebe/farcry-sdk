using System.Xml.Linq;
using JackAll.Core.Format;
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
    public static byte[] Pack(XElement source) => new Packing(source).Write();

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

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

    private sealed class TaskData(string name, ushort blob)
    {
        public string Name { get; } = name;

        public uint Hash { get; } = AiWorkspacePacker.Hash(name);

        public ushort Blob { get; } = blob;

        public ushort Index { get; set; }

        /// <summary>The plans that add this task, in the order they were met.</summary>
        public List<ushort> Owners { get; } = [];

        public ByteWriter Body { get; } = new();
    }

    private sealed class Packing
    {
        private readonly AiPackedRepository _repo = new();
        private readonly Dictionary<uint, TaskData> _tasks = [];
        private readonly Dictionary<uint, ushort> _anchors = [];
        private readonly List<TaskData> _sorted;

        public Packing(XElement source)
        {
            var blobs = new Dictionary<(uint, string), ushort>();
            List<(XElement Instance, TaskData Task)> instances = [];
            foreach (XElement instance in source.Elements())
            {
                uint cls = Hash((string?)instance.Attribute("Class") ?? "");
                byte[] rml = RmlDocument.Serialize(ParametersOf(instance));
                if (!blobs.TryGetValue((cls, Convert.ToBase64String(rml)), out ushort blob))
                {
                    blob = (ushort)_repo.Blobs.Count;
                    blobs.Add((cls, Convert.ToBase64String(rml)), blob);
                    _repo.Blobs.Add(new AiParameterBlob(cls, rml));
                }
                var task = new TaskData((string)instance.Attribute("Name")!, blob);
                instances.Add((instance, _tasks.TryAdd(task.Hash, task) ? task : _tasks[task.Hash]));
            }

            _sorted = [.. _tasks.Values.OrderBy(t => t.Hash)];
            for (int i = 0; i < _sorted.Count; i++)
            {
                _sorted[i].Index = (ushort)i;
            }

            foreach ((XElement instance, TaskData task) in instances)
            {
                foreach (XElement child in instance.Elements())
                {
                    AddConnections(task, child);
                }
            }
        }

        public byte[] Write()
        {
            var buffer = new ByteWriter();
            foreach (TaskData task in _sorted)
            {
                int offset = buffer.Length;
                // Each owner's record was prepended as it was met, so the last one comes first.
                foreach (ushort owner in Enumerable.Reverse(task.Owners))
                {
                    buffer.WriteU8((byte)AiConnectionKind.Owner);
                    buffer.WriteU16(owner);
                }
                buffer.WriteRaw(task.Body.ToArray());
                _repo.Tasks.Add(new AiPackedTask(task.Hash, task.Name, task.Blob, (uint)offset, (ushort)(buffer.Length - offset)));
            }
            _repo.Connections = buffer.ToArray();
            return _repo.Write();
        }

        private void AddConnections(TaskData task, XElement child)
        {
            ByteWriter w = task.Body;
            switch (child.Name.LocalName)
            {
                case "Selectable" when Find((string?)child.Attribute("Task")) is { } target:
                    w.WriteU8((byte)AiConnectionKind.Selectable);
                    w.WriteU16(target.Index);
                    w.WriteU16(AnchorIndex((string)child.Attribute("Filter")!));
                    break;

                case "Anchor" or "Exit" or "Event" when child.HasElements:
                    List<XElement> connections = [.. child.Elements()];
                    w.WriteU8((byte)(child.Name.LocalName switch
                    {
                        "Anchor" => AiConnectionKind.Anchor,
                        "Exit" => AiConnectionKind.Exit,
                        _ => AiConnectionKind.Event,
                    }));
                    w.WriteU16(AnchorIndex((string)child.Attribute("Name")!));
                    w.WriteU16((ushort)connections.Count);
                    foreach (XElement connection in connections)
                    {
                        string target = (string)connection.Attribute("Target")!;
                        w.WriteU16(_tasks[Hash(target)].Index);
                        w.WriteU16(AnchorIndex((string)connection.Attribute("TargetAnchor")!));
                        // 1 when the target lives inside this task, 2 otherwise.
                        w.WriteU8((byte)(target.StartsWith(task.Name, StringComparison.Ordinal) ? 1 : 2));
                    }
                    break;

                case "Add" when Find((string?)child.Attribute("Task")) is { } added:
                    added.Owners.Add(task.Index);
                    break;
            }
        }

        private TaskData? Find(string? name) => name is null ? null : _tasks.GetValueOrDefault(Hash(name));

        private ushort AnchorIndex(string name)
        {
            uint hash = Hash(name);
            if (!_anchors.TryGetValue(hash, out ushort index))
            {
                index = (ushort)_repo.Anchors.Count;
                _anchors.Add(hash, index);
                _repo.Anchors.Add(hash);
            }
            return index;
        }
    }
}
