using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Tools.Ai;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Xml.Linq;

namespace JackAll.Cli.Commands.Ai;

/// <summary>
/// Splits a brain workspace into its BlackBox.AI source and a readable dump of the compiled repository.
/// </summary>
public sealed class AiUnpackCommand : CliCommand<AiUnpackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.ai.rml>")]
        [Description("The brain workspace to unpack.")]
        public string Input { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        AiWorkspaceFile file = AiWorkspaceFile.Read(CliIO.ReadInput(settings.Input));
        string stem = Path.GetFileName(settings.Input).Replace(".ai.rml", "", StringComparison.OrdinalIgnoreCase);

        string sourcePath = CliIO.ResolveOutput(null, settings.Input, stem + ".source.xml");
        CliIO.WriteOutput(sourcePath, file.Source.ToString());
        CliIO.ReportWrote(sourcePath);

        string packedPath = CliIO.ResolveOutput(null, settings.Input, stem + ".packed.xml");
        CliIO.WriteOutput(packedPath, Dump(AiPackedRepository.Read(file.Packed), file.Source).ToString());
        CliIO.ReportWrote(packedPath);
        return 0;
    }

    private static XElement Dump(AiPackedRepository repo, XElement source)
    {
        var names = new Dictionary<uint, string>();
        foreach (string text in source.DescendantsAndSelf().Attributes().Select(a => a.Value))
        {
            names.TryAdd(FcbClassDefinitions.Crc32Ascii(text), text);
        }
        string Name(uint hash) => names.TryGetValue(hash, out string? n) ? n : $"0x{hash:X8}";
        string Anchor(ushort index) => index < repo.Anchors.Count ? Name(repo.Anchors[index]) : $"#{index}";
        string Task(ushort index) => index < repo.Tasks.Count ? repo.Tasks[index].Name : $"#{index}";

        return new XElement("Packed",
            new XElement("Anchors", repo.Anchors.Select((a, i) => new XElement("Anchor", new XAttribute("Index", i), Name(a)))),
            new XElement("Blobs", repo.Blobs.Select((b, i) => new XElement("Blob",
                new XAttribute("Index", i), new XAttribute("Class", Name(b.ClassHash)),
                RmlDocument.Deserialize(b.Rml)))),
            repo.Tasks.Select((t, i) => new XElement("Task",
                new XAttribute("Index", i), new XAttribute("Name", t.Name),
                new XAttribute("Hash", $"{t.NameHash:X8}"),
                new XAttribute("HashMatches", FcbClassDefinitions.Crc32Ascii(t.Name) == t.NameHash),
                new XAttribute("Blob", t.Blob),
                new XAttribute("Offset", t.ConnectionOffset),
                repo.ConnectionsOf(t).Select(c => new XElement(c.Kind.ToString(),
                    new XAttribute("Source", c.Kind == AiConnectionKind.Owner ? Task(c.Source) : Anchor(c.Source)),
                    c.Targets.Select(x => new XElement("To",
                        new XAttribute("Task", Task(x.Task)),
                        new XAttribute("Anchor", Anchor(x.Anchor)),
                        new XAttribute("Flag", x.Flag))))))));
    }
}
