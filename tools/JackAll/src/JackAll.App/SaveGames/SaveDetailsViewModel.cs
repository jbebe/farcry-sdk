using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Sav;

namespace JackAll.App.SaveGames;

/// <summary>
/// Display-only knowledge of the small, fixed vocabulary of name-hashes
/// <c>CPersistenceDB::SaveDB</c> uses for its own structural tags — a completely different, disjoint
/// hash namespace from <c>binary_classes.xml</c> (which only catalogs `entitylibrary.fcb`-style data
/// classes; see reverse/dunia/savegame_format.md's "Confirmed: the persistence tag vocabulary"
/// section). Recovered by reading the literal `Tag_*` global symbol names off `CPersistenceDB::SaveDB`'s
/// decompile in `FarCry2_server` and testing whether their own name, CRC32-hashed the same way the
/// engine hashes everything else, actually appears in real save data — it does, thousands of times
/// each, which is what confirms these symbol names really are the runtime string content and not just
/// a decompiler/debug-info label. Deliberately NOT folded into <see cref="FcbClassDefinitions"/> or
/// <see cref="FcbXml"/> itself: those are shared, round-trip-critical machinery the Files tab's real
/// mod-editing depends on, and this dictionary is unverified enough (hand-derived from a handful of
/// decompiled functions, each entry only kept once its hash was confirmed to actually appear in a
/// real save) that it belongs kept separate and display-only - consulted by
/// <see cref="SaveGameXmlRenderer"/> while it builds the tree, not applied as a text patch afterward.
/// </summary>
internal static class SaveGamePersistenceTags
{
    public static readonly IReadOnlyDictionary<uint, string> ByHash = new Dictionary<uint, string>
    {
        // "Id" (0x2ABD43F2), "EntityId" (0x0F5E4BAA), "State" (0x6252FDFF) and "Description"
        // (0xEB78CFF1) are deliberately NOT listed here even though SaveDB's decompile named all of
        // them too - binary_classes.xml happens to independently know all four (real, unrelated
        // classes just happen to also declare members/classes with those same common names, which
        // hash identically), so SaveGameFieldCatalog's flat lookup already gives them a real wire
        // type, not just a bare name label - strictly more informative, and checked first by
        // SaveGameXmlRenderer, so relisting them here would just be redundant clutter.
        [0xA9100FC2] = "HierarchyId",
        [0x9C989AA7] = "Record",
        [0x7A2B069C] = "HierarchyRecord",
        [0xA99A06B3] = "Entities",
        [0x788BAA0D] = "Hierarchy",
        [0x7C1C0FBA] = "HierarchiesQueue",
        [0x5134EF37] = "OmniEntities",

        // CPersistenceDB::CPersistenceDBRec::RegisterProperties / CBindingHierarchyDBRec's own
        // (@ 0x09679a93 / 0x09679b22 in FarCry2_server) - real registered member names, not
        // ISerializableNode navigation tags like the ones above. "BindingHierarchy" (also registered
        // there) never showed up in a real save's dump - plausibly a child-object reference rather
        // than a plain scalar value, so deliberately left out rather than included unverified.
        // Neither of these two is in binary_classes.xml either, unlike the four excluded above.
        [0x65A0E5B6] = "MemoryUsage",
        [0x4A1FC981] = "PersistType",
    };
}

/// <summary>
/// Display-only decoding of `BinHex` value payloads that are actually readable text. A value with no
/// verified <see cref="FcbMemberType"/> comes out as opaque hex by default (see the "why
/// binary_classes.xml can't resolve any of it" section in reverse/dunia/savegame_format.md), even though
/// a large fraction of them are plainly the engine's own null-terminated ASCII/UTF8 strings under the
/// hex — buddy names, state tags like `"NOT_SET"`, world/DLC ids, and so on, all directly observed by eye
/// while investigating this format. Only that one unambiguous case is decoded — a null-terminated run of
/// printable ASCII — deliberately not attempting to guess numeric types (Float/UInt32/...) for other byte
/// lengths, since which numeric interpretation (if any) is correct is genuinely unknown per field and a
/// wrong guess would mislead more than the raw hex does.
/// </summary>
internal static class SaveGameValueDecoder
{
    /// <summary>Null-terminated, every preceding byte printable ASCII. Rejects anything with embedded
    /// control bytes rather than decoding them as text: <see cref="System.Xml.Linq.XElement"/> can't
    /// serialize raw 0x00/control characters at all, so a wrong "this is a string" guess has to be
    /// caught here, not discovered as a crash when the document renders.</summary>
    public static bool TryDecodeReadableString(byte[] value, out string text)
    {
        text = "";
        if (value.Length < 1 || value[^1] != 0)
        {
            return false; // not null-terminated - not the engine's string convention (see fcb_format.md)
        }

        for (int i = 0; i < value.Length - 1; i++)
        {
            if (value[i] is < 0x20 or > 0x7E)
            {
                return false; // not printable ASCII
            }
        }

        text = Encoding.ASCII.GetString(value, 0, value.Length - 1);
        return true;
    }
}

/// <summary>
/// Loads the embedded PersistenceDB `.fcb` tree for one selected save and renders it as a single XML
/// document for the Saves tab's details sidebar — the whole tree, unconditionally, no splitting into
/// fragments and no size gate. A real save's tree can be tens of thousands of objects (see
/// reverse/dunia/savegame_format.md), so this can be a genuinely huge document; shown as-is anyway,
/// on the reasoning that a slow-but-complete view beats a dead end. This class itself stays read-only —
/// it only ever renders <see cref="DocumentXml"/> for display; editing/writing a save back to disk
/// happens in <c>XmlEditorTabViewModel</c>/<c>SaveGameDocument.WriteFcbRoot</c>, from the
/// <c>FcbObject</c> tree that tab mutates directly, never by re-parsing this class's rendered XML.
/// </summary>
public sealed class SaveDetailsViewModel : INotifyPropertyChanged
{
    private static Lazy<FcbClassDefinitions> Definitions => FcbDefinitionsProvider.Value;

    public SaveRow Save { get; }

    private readonly Func<FcbStringCorpus> _resolveEntityLibraryCorpus;

    private bool _isLoading = true;
    private string _statusText = "Decoding this save's data…";
    private string? _documentXml;

    /// <param name="resolveEntityLibraryCorpus">
    /// Supplies the reverse hash -&gt; string dictionary <see cref="SaveGameReferenceResolver"/> uses
    /// (see that class's remarks) - a delegate rather than the corpus itself so the caller can build it
    /// lazily/once per session (harvesting it is a real cost) without this view model needing to know
    /// how or when that happens. Called on the same background thread the rest of <see cref="LoadAsync"/>
    /// already runs on.
    /// </param>
    public SaveDetailsViewModel(SaveRow save, Func<FcbStringCorpus> resolveEntityLibraryCorpus)
    {
        Save = save;
        _resolveEntityLibraryCorpus = resolveEntityLibraryCorpus;
        _ = LoadAsync();
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowDocument)); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string? DocumentXml
    {
        get => _documentXml;
        private set { _documentXml = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowDocument)); }
    }

    public bool ShowDocument => !IsLoading && DocumentXml is not null;

    private async Task LoadAsync()
    {
        try
        {
            FcbObject root = await Task.Run(() => SaveGameDocument.ReadFcbRoot(Save.Info));
            string xml = await Task.Run(
                () => SaveGameXmlRenderer.Render(root, Definitions.Value, _resolveEntityLibraryCorpus()));
            DocumentXml = xml;
            StatusText = "Ready.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't decode this save's data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
