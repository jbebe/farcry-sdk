using JackAll.Core.Xrefs;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// The complete extractor set, Core's and Tools' together.
/// </summary>
/// <remarks>
/// Lives here rather than in <c>JackAll.Core</c> because this is the lowest layer that can see both
/// halves: the `.fcb`/`depload`/text decoders are in Core, the `.mgb`/`.spk`/`.xbm`/`.xbg` ones are
/// here, and Core can't reference this project. <see cref="ReferenceIndexer"/> takes the list as a
/// parameter for exactly that reason, so both the app and the CLI just ask for
/// <see cref="All"/> instead of assembling it themselves and drifting apart.
///
/// Order matters only where two extractors could claim the same file, which today is only the
/// `.dat` case - <see cref="DepLoadReferenceExtractor"/> matches on the `_depload.dat` filename
/// suffix and nothing else claims plain `.dat`, so the list is otherwise free to be read as a
/// catalogue.
/// </remarks>
public static class ReferenceExtractors
{
    public static IReadOnlyList<IReferenceExtractor> All { get; } =
    [
        new FcbReferenceExtractor(),
        new DepLoadReferenceExtractor(),
        new MgbReferenceExtractor(),
        new XbmReferenceExtractor(),
        new XbgReferenceExtractor(),
        new SpkReferenceExtractor(),
        new SbaoReferenceExtractor(),
        new RmlReferenceExtractor(),
        new TextReferenceExtractor(),
    ];
}
