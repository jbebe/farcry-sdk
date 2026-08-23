using JackAll.Core.Format;
using JackAll.Core.Xrefs;

namespace JackAll.Tools.Fc2Model;

/// <summary>
/// How many files use a path, for deciding what a pack lets an editor change.
/// </summary>
/// <remarks>
/// The directory rule a pack falls back on gets this wrong in one common direction: a material
/// pooled in <c>graphics\_materials</c> is not in the model's folder, so it reads as shared even
/// when a single model uses it - and that is most of what a modeler wants to edit.
/// <para>
/// Under-counting is the dangerous direction, because a low count is what promotes a file to
/// <c>owned</c>. Two things keep it honest. The model itself is always counted, whether or not the
/// index holds its edges, so a count of one can only ever mean this model - never one other file
/// with the model missing. And the count comes from the whole reference graph rather than a walk
/// over meshes: a weapon archetype names <c>bullettracer_d.xbt</c> from an <c>.fcb</c> field and no
/// mesh mentions it at all.
/// </para>
/// </remarks>
public static class ReferenceUsage
{
    /// <summary>
    /// The edge kinds where the referencing file is itself the user.
    /// </summary>
    /// <remarks>
    /// <see cref="RefKind.TextPath"/> is deliberately absent. Every world ships a generated
    /// <c>_depload.xml</c> beside its <c>.dat</c>, restating the same dependency list as text, so
    /// counting those makes every material a level loads look used by dozens of files: the ak47's
    /// materials count 47 that way and 8 once the restatements are dropped. What is lost with them
    /// is a path named in an <c>.rml</c> or a Lua script, which is not a rendering use.
    /// </remarks>
    private static readonly RefKind[] Direct =
    [
        RefKind.XbgMaterial, RefKind.XbmTexture, RefKind.MgbTexture,
        RefKind.FcbPathValue, RefKind.FcbNameValue, RefKind.MgbNameId,
    ];

    /// <summary>
    /// A counter for <see cref="Fc2ModelBuilder.Build"/>, or null when the index cannot answer.
    /// </summary>
    /// <remarks>
    /// Null rather than a counter that returns zero: an empty index would say every file is
    /// unreferenced, which promotes all of them. The pack falls back to the directory rule, which is
    /// wrong in the safe direction.
    /// </remarks>
    public static Func<string, int>? Counter(ReferenceIndex index, string modelPath)
    {
        if (index.EdgeCount == 0)
        {
            return null;
        }

        uint model = NameHash.Compute(modelPath);
        return path => Users(index, path, model).Count;
    }

    /// <summary>
    /// Every file that uses this one, by hash.
    /// </summary>
    /// <remarks>
    /// A <c>depload.dat</c> is a level's manifest of what to load, not a user - counting it would
    /// say a material is used by every level the weapon appears in. But it sites each dependency by
    /// the parent resource that pulled it in, and that parent is the user, so those edges are
    /// counted by their site rather than their source. It is the one place the index knows a user
    /// that no file's own bytes name.
    /// </remarks>
    public static HashSet<uint> Users(ReferenceIndex index, string path, uint model)
    {
        HashSet<uint> users = [];
        foreach (RefEdge edge in index.ReferencesTo(RefSpace.FilePath, NameHash.Compute(path)))
        {
            if (edge.Kind == RefKind.DepLoadDependency)
            {
                users.Add(edge.SiteKey);
            }
            else if (Direct.Contains(edge.Kind))
            {
                users.Add(edge.SourceFile);
            }
        }

        // The closure was walked from the model, so the model uses this whether or not the index
        // holds that edge. Counting it is what makes a count of one mean "only this model".
        users.Add(model);
        return users;
    }
}
