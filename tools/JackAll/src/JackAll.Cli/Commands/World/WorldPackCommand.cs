using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;
using JackAll.Tools.World;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.World;

/// <summary>
/// Writes a mod layer in the form it ships: every new world it creates built from its fragments into
/// whole files, everything else copied as it is.
/// </summary>
/// <remarks>Building happens here, once, so installing the mod is only copying. See
/// docs/jackall/world/new-worlds.md.</remarks>
public sealed class WorldPackCommand : CliCommand<WorldPackCommand.Settings>
{
    public sealed class Settings : GameCommandSettings
    {
        [CommandArgument(0, "<mod>")]
        [Description("The mod folder or .zip to pack, such as JackAll's workspace.")]
        public string Mod { get; init; } = null!;

        [CommandOption("-b|--base <path>")]
        [Description("A mod the packed one builds on, such as the mod whose world it changes. Repeatable, in load order.")]
        public string[] Bases { get; init; } = [];

        [CommandOption("-o|--out <folder>")]
        [Description("An empty folder to write the packed layer to.")]
        public string Out { get; init; } = null!;
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        LayerFolder.RequireEmpty(settings.Out);
        IModLayer mod = ModPipeline.OpenLayer(settings.Mod);
        var progress = new SyncProgress(JsonOutput.Report);
        FcbClassDefinitions defs = BundledAssets.LoadFcbClasses();
        GameInstall install = settings.OpenInstall();

        using GameVfs vfs = GameVfs.Load(
            install, BundledAssets.LoadNames(), GameCache.Load(install.CacheFile), defs, vanillaPatch: true);
        vfs.Rebuild([.. settings.Bases.Select(ModPipeline.OpenLayer), mod]);
        SortedDictionary<string, byte[]> packed = WorldPack.Pack(mod, vfs, defs, progress);

        LayerFolder.Write(settings.Out, packed);
        foreach (string plugin in mod.PluginPaths)
        {
            CliIO.WriteOutput(Path.Combine(settings.Out, "plugins", plugin), mod.ReadPlugin(plugin));
        }
        if (settings.Json)
        {
            JsonOutput.Write(new { ok = true, files = packed.Count, plugins = mod.PluginPaths.Count });
            return 0;
        }
        AnsiConsole.MarkupLine($"{packed.Count:N0} files, {mod.PluginPaths.Count} plugin files.");
        CliIO.ReportWrote(settings.Out);
        return 0;
    }
}
