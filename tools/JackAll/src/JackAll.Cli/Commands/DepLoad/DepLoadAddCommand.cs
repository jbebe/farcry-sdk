using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using JackAll.Core.Mods;

using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.DepLoad;

/// <summary>Registers a resource as a dependency of another in a `depload.dat`.</summary>
/// <remarks>The edit a mod adding new content needs. An animation clip only loads if it is listed
/// under the `CAnimationPackageResource` its weapon names, so a clip at a path the game never shipped
/// has to be added to that package. See docs/docs/file-formats/depload.md.</remarks>
public sealed class DepLoadAddCommand : CliCommand<DepLoadAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file_depload.dat>")]
        [Description("The dependency index to edit.")]
        public string Input { get; init; } = null!;

        [CommandOption("-p|--parent <NAME>")]
        [Description("What depends on the child: an animation package name, a game path, or a hex CRC32.")]
        public string Parent { get; init; } = null!;

        [CommandOption("-c|--child <PATH>")]
        [Description("The resource being registered: a game path, or a hex CRC32.")]
        public string Child { get; init; } = null!;

        [CommandOption("-t|--type <CLASS>")]
        [Description("The child's resource class, e.g. CAnimationResource.")]
        public string Type { get; init; } = "CAnimationResource";

        [CommandOption("-o|--out <PATH>")]
        [Description("Output path (default: edits the input in place).")]
        public string? Out { get; init; }

        [CommandOption("--fragment")]
        [Description("Write only the edited resource's own entry, to stage in a mod layer under "
                     + @"mods\<container path>\<hash>.xml, instead of the whole rebuilt file.")]
        public bool Fragment { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        uint parent = Resolve(settings.Parent);
        uint child = Resolve(settings.Child);
        uint type = DepLoadTypes.Hash(settings.Type);
        if (DepLoadTypes.NameOf(type) is null)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]'{settings.Type.EscapeMarkup()}' is not a resource class this format is known to "
                + "use.[/] Adding it anyway - check the spelling if the game ignores the entry.");
        }

        DepLoadFile file = DepLoadDocument.Decode(CliIO.ReadInput(settings.Input));
        DepLoadFile edited = DepLoadEdit.AddChild(file, parent, child, type);

        string outPath;
        if (settings.Fragment)
        {
            // --parent is usually the package's own name, so the staged file can read as
            // dragunov.3882209901.xml rather than a bare number. The label is cosmetic either way.
            string id = DepLoadContainerSplitter.IdOf(parent, NameFor(settings.Parent, parent));
            outPath = CliIO.ResolveOutput(settings.Out, settings.Input, id);
            CliIO.WriteOutput(outPath,
                DepLoadXml.FragmentToXml(edited.Parents.First(p => p.Hash == parent)));
        }
        else
        {
            outPath = settings.Out ?? settings.Input;
            CliIO.WriteOutput(outPath, DepLoadDocument.Encode(edited));
        }

        AnsiConsole.MarkupLine(
            $"{child:X8} [dim]({settings.Type.EscapeMarkup()})[/] now listed under {parent:X8}.");
        CliIO.ReportWrote(outPath);
        return 0;
    }

    /// <summary>
    /// A resource's CRC32. Hex is taken as the hash itself; anything else is hashed as a path - which
    /// is also how a package name resolves, since the engine hashes those the same way.
    /// </summary>
    private static uint Resolve(string nameOrHash)
        => CliIO.TryParseHash(nameOrHash) ?? NameHash.Compute(nameOrHash);

    /// <summary>What the user called this resource, or nothing when they gave its hash instead.</summary>
    private static string? NameFor(string typed, uint hash)
        => CliIO.TryParseHash(typed) == hash ? null : typed;
}
