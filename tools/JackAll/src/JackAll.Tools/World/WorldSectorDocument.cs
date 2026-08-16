using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// One <c>worldsector&lt;id&gt;.data.fcb</c> in the loaded world: the deserialized pristine tree plus
/// where it came from. The live <see cref="WorldEntity"/> pool references back here so edits know
/// which file to rebuild and stage.
/// </summary>
public sealed class WorldSectorDocument
{
    /// <summary>Game-relative source path, e.g. <c>levels\w1_c_2\generated\worldsectors\worldsector2989.data.fcb</c>.</summary>
    public required string SourcePath { get; init; }

    /// <summary>The sector's id on its map's grid.</summary>
    public required int SectorId { get; init; }

    /// <summary>The tree exactly as loaded - never mutated by edits; saves rebuild a clone.</summary>
    public required FcbObject PristineRoot { get; init; }
}
