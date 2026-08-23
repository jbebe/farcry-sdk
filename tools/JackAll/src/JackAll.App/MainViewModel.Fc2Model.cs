using System.IO;
using JackAll.Core.Vfs;
using JackAll.Tools.Fc2Model;

namespace JackAll.App;

/// <summary>
/// Building and applying <c>.fc2model</c> packs - the editor-facing half of the model pipeline.
/// </summary>
/// <remarks>
/// A pack is the only thing a modeler's editor ever sees: everything in it is decoded, so nothing
/// outside JackAll needs a line of Dunia format code. Exporting collects a model and its closure;
/// applying encodes what came back and stages it into the workspace, which is the same route every
/// other edit in the app takes.
/// <para>
/// Kept in its own partial for the same reason the xrefs are - a self-contained feature with its own
/// vocabulary, and nothing in it interleaves with mod layering or file browsing.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>Whether "Export as .fc2model" applies to the selected row.</summary>
    public bool SelectionIsModel
        => SelectedFile is { NameIsKnown: true, Type.Extension: "xbg" };

    /// <summary>
    /// Collects the selected model and everything it names into a pack.
    /// </summary>
    /// <remarks>
    /// Ownership uses the app's own reference index when it has one. Without it every file outside
    /// the model's folder reads as shared, which refuses edits that would have been safe - wrong,
    /// but in the direction that cannot break another model.
    /// </remarks>
    public Fc2ModelBundle BuildPack(VfsFile model, IEnumerable<string>? clips = null)
        => Fc2ModelBuilder.Build(
            model.Path,
            ReadByPath,
            ReferenceUsage.Counter(_xrefIndex, model.Path),
            clips);

    /// <summary>Every animation bank that names this model, which is not something its mesh says.</summary>
    public List<string> FindClips(VfsFile model)
        => ClipSearch.For(
            model.Path,
            [.. AllKnownPaths.Where(path => path.EndsWith(".mab", StringComparison.OrdinalIgnoreCase))],
            ReadByPath);

    /// <summary>
    /// What applying this pack would write, so it can be shown before anything is.
    /// </summary>
    /// <remarks>
    /// Only entries an editor changed are produced. A texture travels as PNG, so re-encoding an
    /// untouched one would compress it again on every apply and decay it across saves.
    /// </remarks>
    public static List<Fc2ModelOutput> PlanPack(Fc2ModelBundle bundle)
        => Fc2ModelApplier.Outputs(bundle);

    /// <summary>
    /// Stages a pack's edits into the workspace, and reports what it wrote.
    /// </summary>
    /// <remarks>
    /// Through the same <see cref="Core.Mods.FolderModLayer.Stage"/> every other edit uses, so a
    /// packed model is an ordinary workspace override afterwards - revertable, buildable into a mod,
    /// and visible in the file tree exactly like a hand-replaced file.
    /// </remarks>
    public int ApplyPack(Fc2ModelBundle bundle)
    {
        if (Workspace is null)
        {
            throw new InvalidOperationException("The workspace is not available.");
        }

        List<Fc2ModelOutput> outputs = PlanPack(bundle);
        foreach (Fc2ModelOutput output in outputs)
        {
            Workspace.Stage(
                Core.Format.NameHash.Compute(output.Path),
                output.Path,
                Path.GetExtension(output.Path).TrimStart('.'),
                output.Content);
        }
        return outputs.Count;
    }
}
