using System.Diagnostics.CodeAnalysis;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.SaveGames;

/// <summary>
/// Display-only resolution of a save's own hashes against a reverse hash -&gt; string dictionary
/// harvested from String-typed values in the game's entitylibrary <c>.fcb</c> files (see
/// <see cref="FcbStringCorpus"/>) - a distinct axis from <see cref="SaveGameFieldCatalog"/>, which
/// resolves *field names* via <c>binary_classes.xml</c>. A save's PersistenceDB tree never carries
/// field names entitylibrary would recognize (see reverse/dunia/savegame_format.md), but plenty of its
/// *values* - entity names, archetype ids, plan names, and similar - are themselves nothing more than
/// the CRC32 of a string a sibling, fully class-resolved entitylibrary <c>.fcb</c> still spells out in
/// the clear. Checked by <see cref="SaveGameXmlRenderer"/> for any still-unresolved 4-byte value - the
/// shape a genuine Hash-typed reference has on the wire - left completely untouched when there's no
/// match. There's no base-.fcb-format equivalent of this (an ordinary .fcb never resolves a value's own
/// *content*, only field names/types), so a match is surfaced as an additive <c>ref="..."</c> attribute
/// rather than replacing anything - a matched hash means "some string in entitylibrary hashes the same",
/// not certainty this specific field really means that, and a corpus-internal collision
/// (<see cref="FcbStringCorpus.Ambiguous"/>) is skipped outright rather than guessing.
/// </summary>
internal static class SaveGameReferenceResolver
{
    public static bool TryResolve(FcbStringCorpus corpus, uint hash, [NotNullWhen(true)] out string? name)
    {
        name = null;
        return !corpus.Ambiguous.Contains(hash) && corpus.ByHash.TryGetValue(hash, out name);
    }
}
