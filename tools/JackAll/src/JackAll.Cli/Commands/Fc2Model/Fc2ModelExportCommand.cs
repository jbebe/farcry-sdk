using System.ComponentModel;
using JackAll.Cli.Commands.Mod;
using JackAll.Cli.Commands.Xref;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Fc2Model;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fc2Model;

/// <summary>
/// Collects a model and everything it references out of a game install into one decoded pack.
/// </summary>
/// <remarks>
/// This is the half of the flow that faces the editor: the pack it produces carries no Dunia format
/// at all, so whatever opens it needs no format code. Applying one back is <c>fc2model apply</c>.
/// </remarks>
public sealed class Fc2ModelExportCommand : CliCommand<Fc2ModelExportCommand.Settings>
{
    /// <summary>
    /// The body a pack carries for its clips to pose.
    /// </summary>
    /// <remarks>
    /// The arms a player sees are whichever protagonist they picked, so there is no one right
    /// answer; this is the smallest of the fourteen. All of them skin to the same rig, and none has
    /// a sibling skeleton of its own.
    /// </remarks>
    private const string DefaultActor = @"graphics\actors\principal_yabeck\yabek.xbg";

    private const string DefaultActorRig = @"graphics\characters\_common\pelvis_ref.skeleton";

    public sealed class Settings : XrefFileSettings
    {
        [CommandArgument(0, "<model>")]
        [Description("The model's game-relative path, e.g. graphics/weapons/primary/ak47/ak47.xbg. "
                   + "A path to a loose .xbg works too, resolving what it names out of the tree it "
                   + "sits in - no install needed.")]
        public string Model { get; init; } = string.Empty;

        [CommandOption("-o|--out <file.fc2model>")]
        [Description("Where to write the pack (default: the model's name beside the working directory).")]
        public string? Out { get; init; }

        [CommandOption("--clip <path>")]
        [Description("An animation bank to carry along, by game path. Repeatable.")]
        public string[] Clip { get; init; } = [];

        [CommandOption("--rig <path>")]
        [Description("The rig to carry, by game path. Defaults to the one beside the model; a "
                   + "character has none of its own and shares pelvis_ref.skeleton.")]
        public string? Rig { get; init; }

        [CommandOption("--clips")]
        [Description("Carry every animation bank that names this model. Reads every bank in the install.")]
        public bool Clips { get; init; }

        [CommandOption("--actor <path>")]
        [Description("The body to carry alongside the clips, so the hands they pose can be seen and "
                   + "animated against. Defaults to a playable protagonist; mesh and rig only, no "
                   + "materials or textures.")]
        public string Actor { get; init; } = DefaultActor;

        [CommandOption("--no-actor")]
        [Description("Leave the body out. The clips still carry the character's motion, but there is "
                   + "nothing to play it on.")]
        public bool NoActor { get; init; }

        [CommandOption("--actor-rig <path>")]
        [Description("The actor's rig. Every skinned character shares pelvis_ref, so there is "
                   + "nothing beside the model to find.")]
        public string ActorRig { get; init; } = DefaultActorRig;

        /// <summary>
        /// <c>--game</c> is not needed when the model argument is a file that exists.
        /// </summary>
        /// <remarks>
        /// Packing a loose <c>.xbg</c> resolves what it names out of the folder tree it sits in, so
        /// there is no install to point at - which is what an extracted export, a test fixture and a
        /// bug report all look like.
        /// </remarks>
        public override Spectre.Console.ValidationResult Validate()
            => File.Exists(Model) ? Spectre.Console.ValidationResult.Success() : base.Validate();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        return File.Exists(settings.Model) ? FromFolder(settings) : FromInstall(settings);
    }

    /// <summary>
    /// Pack a loose <c>.xbg</c>, resolving what it names out of the tree it sits in.
    /// </summary>
    /// <remarks>
    /// For an extracted export rather than an installed game - which is what a test fixture and a
    /// bug report both look like. Ownership falls back to the directory rule, because a folder of
    /// files is not the whole game and a count taken over it would promote things that are shared.
    /// </remarks>
    private int FromFolder(Settings settings)
    {
        string full = Path.GetFullPath(settings.Model);
        string? root = GraphicsRoot(full);
        if (root is null)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{full} is not under a 'graphics' folder[/], so nothing can resolve the materials and textures it names by game path.");
            return 1;
        }

        // Indexed by file name rather than by path: a model names its materials however the
        // authoring tool spelled them, and a loose export is not case-consistent about it.
        Dictionary<string, string> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            byName[Path.GetFileName(path)] = path;
        }

        byte[]? Read(string gamePath)
        {
            // A path that exists is read where it is: a clip written out of an editor sits wherever
            // that editor put it, not inside the tree the model came from.
            if (File.Exists(gamePath))
            {
                return File.ReadAllBytes(gamePath);
            }
            string name = Path.GetFileName(gamePath.Replace('\\', '/'));
            return byName.TryGetValue(name, out string? found) ? File.ReadAllBytes(found) : null;
        }


        string model = Relative(root, full);
        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(
            model, Read, null, settings.Clip, settings.Rig,
            settings.NoActor ? null : settings.Actor, settings.ActorRig);
        return Write(settings, bundle, model);
    }

    /// <summary>The folder holding the model's nearest <c>graphics</c> ancestor, if there is one.</summary>
    private static string? GraphicsRoot(string modelPath)
    {
        for (DirectoryInfo? at = Directory.GetParent(modelPath); at is not null; at = at.Parent)
        {
            if (at.Name.Equals("graphics", StringComparison.OrdinalIgnoreCase))
            {
                return at.Parent?.FullName;
            }
        }
        return null;
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private int FromInstall(Settings settings)
    {
        GameInstall install = settings.OpenInstall();
        using GameVfs vfs = GameVfs.Load(
            install, BundledAssets.LoadNames(), GameCache.Load(install.CacheFile),
            BundledAssets.LoadFcbClasses(), new SyncProgress(JsonOutput.Report), includeFragments: false);

        ReferenceIndex index = ReferenceIndex.Load(settings.ResolveIndexPath());
        Func<string, int>? usage = ReferenceUsage.Counter(index, settings.Model);
        if (usage is null)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No reference index[/]: ownership falls back to the directory rule, which "
                + "marks a pooled material shared even when only this model uses it. Run "
                + "'jackall-cli xref build' to get counts.");
        }

        List<string> clips = Clips(vfs, settings);
        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(
            settings.Model, vfs.ReadByPath, usage, clips, settings.Rig,
            settings.NoActor ? null : settings.Actor, settings.ActorRig);
        return Write(settings, bundle, settings.Model);
    }

    private static int Write(Settings settings, Fc2ModelBundle bundle, string model)
    {
        string output = settings.Out
            ?? Path.GetFileNameWithoutExtension(model) + Fc2ModelBundle.Extension;
        bundle.Save(output);

        int carried = bundle.Manifest.Entries.Count(entry => entry.Kind == Fc2ModelKind.Clip);
        if (settings.Json)
        {
            JsonOutput.Write(new
            {
                ok = true,
                model = bundle.Manifest.Model,
                entries = bundle.Manifest.Entries.Count,
                clips = carried,
                output,
            });
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Packed {bundle.Manifest.Entries.Count} files for {bundle.Manifest.Model}");
        if (carried > 0)
        {
            AnsiConsole.MarkupLineInterpolated($"  including {carried} animation bank(s)");
        }
        CliIO.ReportWrote(output);
        return 0;
    }

    /// <summary>
    /// Which banks to carry.
    /// </summary>
    /// <remarks>
    /// Nothing in a mesh names its animation - a weapon's motion is filed under the character
    /// animations - so there is no closure to walk. Naming banks one by one works when the paths are
    /// known; the search is for when they are not, and it asks the banks rather than guessing at
    /// folder names - see <see cref="ClipSearch"/>.
    /// </remarks>
    private static List<string> Clips(GameVfs vfs, Settings settings)
    {
        var clips = new List<string>(settings.Clip);
        if (!settings.Clips)
        {
            return clips;
        }

        List<string> banks = [.. vfs.Files.Values
            .Select(file => file.Path)
            .Where(path => path.EndsWith(".mab", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)];
        clips.AddRange(ClipSearch.For(settings.Model, banks, vfs.ReadByPath));
        return clips;
    }
}
