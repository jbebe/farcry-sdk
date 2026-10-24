using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.NavMesh;

/// <summary>Generates a level's navmesh from its terrain and the obstacles placed on it.</summary>
/// <remarks>The input is a folder laid out like <c>data_win32</c>, such as a mod layer's
/// <c>mods</c> folder. See docs/docs/file-formats/nvm.md.</remarks>
public sealed class NavMeshBuildCommand : CliCommand<NavMeshBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<folder>")]
        [Description(@"A folder holding levels\<level>\ and worlds\<world>\ as the game lays them out.")]
        public string Root { get; init; } = null!;

        [CommandOption("-l|--level <NAME>")]
        [Description("The level to build (default: the only one in the folder).")]
        public string? Level { get; init; }

        [CommandOption("-w|--world <NAME>")]
        [Description("The world whose entity library and sectorsdep.fcb to use (default: the level's name).")]
        public string? World { get; init; }

        [CommandOption("-o|--out <folder>")]
        [Description("Where to write, in the same layout (default: the input folder).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(settings.Root);
        string outRoot = settings.Out is null ? root : Path.GetFullPath(settings.Out);
        string[] files = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('/', '\\'))];
        byte[]? Read(string path) => File.Exists(Path.Combine(root, path)) ? File.ReadAllBytes(Path.Combine(root, path)) : null;

        IReadOnlyList<TerrainMap> maps = TerrainMap.Discover(files);
        TerrainMap map = settings.Level is { } level
            ? maps.FirstOrDefault(m => m.Name.Equals(level, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"No level named {level} has terrain under {root}.")
            : maps.Count == 1
                ? maps[0]
                : throw new InvalidOperationException(
                    $"{maps.Count} levels have terrain under {root}; name one with --level.");
        string world = settings.World ?? map.Name;
        if (Read(ArchetypeIndex.BaseLayer(world).Path) is null)
        {
            throw new InvalidOperationException($"{ArchetypeIndex.BaseLayer(world).Path} is not under {root}.");
        }

        var progress = new Progress<string>(line => AnsiConsole.MarkupLine($"[dim]{line.EscapeMarkup()}[/]"));
        (Dictionary<int, NvmSector> sectors, int obstacles) = NavMeshGenerator.BuildMap(map, world, Read, progress);

        foreach ((string sdat, int id) in map.Sectors)
        {
            string path = Path.Combine(outRoot, WorldNavMesh.PathOf(sdat, id));
            CliIO.EnsureDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, NvmSectorFile.Encode(sectors[id]));
        }
        AnsiConsole.MarkupLine(
            $"{sectors.Count} sectors, {sectors.Values.Sum(s => s.Nodes.Length):N0} triangles, {obstacles} obstacles cut out.");

        string dep = $@"worlds\{world}\generated\{world}.sectorsdep.fcb";
        if (Read(dep) is { } depBytes)
        {
            FcbObject sectorsDep = FcbDocument.Deserialize(depBytes);
            int marked = SectorsDep.Mark(sectorsDep, SectorsDep.HasNavMesh, sectors.Keys.ToHashSet());
            string depOut = Path.Combine(outRoot, dep);
            CliIO.WriteOutput(depOut, FcbDocument.Serialize(sectorsDep));
            AnsiConsole.MarkupLine($"HasNavMesh set on {marked} of {sectors.Count} sectors in {dep.EscapeMarkup()}.");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[yellow]{dep.EscapeMarkup()} is not under the folder.[/] The game opens a sector's navmesh only "
                + "when its sectorsdep entry says HasNavMesh.");
        }
        CliIO.ReportWrote(outRoot);
        return 0;
    }
}
