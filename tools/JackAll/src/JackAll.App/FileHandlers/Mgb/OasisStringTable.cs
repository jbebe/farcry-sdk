using System.Globalization;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;

namespace JackAll.App.FileHandlers.Mgb;

/// <summary>
/// The game's localised string table (<c>languages\english\oasisstrings.rml</c>), key → text.
/// </summary>
/// <remarks>
/// One instance per process, built on first use and then kept: the table is ~1 MB with ~11,500
/// entries, which is nothing to hold but enough to be worth not re-parsing per editor tab. The
/// source is supplied by the host once the merged filesystem exists (see <c>MainWindow.OnLoaded</c>)
/// rather than read from disk here, so a mod's override resolves through its version like every
/// other file.
///
/// The <c>.rml</c> is the only source read, and the <c>.xml</c> beside it in <c>common.dat</c> is
/// deliberately ignored. The two have identical content on a fresh install (11,369 strings), but
/// <c>patch.dat</c> ships an updated <c>.rml</c> and *no* <c>.xml</c> at all, so the <c>.xml</c> is a
/// stale pre-patch leftover. The patched table is a straight overwrite rather than a delta - every
/// one of the base table's 11,369 (section, key) pairs is still in it, with 136 added and 9
/// retranslated (<c>DLC1;DESC</c> goes from "PLACEHOLDER TEST DO NOT TRANSLATE" to real text) - so
/// there is nothing in the <c>.xml</c> to fall back to or merge in, and reading it could only
/// downgrade a string that the patch, or a localisation mod, had already fixed.
///
/// An entry's <c>enum</c> attribute is its key, and it is a *string*, not a number: most are
/// symbolic (<c>MAPINFO_NAME_TITLE</c>), a minority are decimal (<c>5153370</c> - the subtitle
/// lines). Two further spellings are folded in: <c>Section;KEY</c>, which is how a <c>.mgb</c> text
/// widget scopes a key to one of the file's <c>&lt;section&gt;</c> blocks (<c>Generic;LOADING</c>);
/// and <c>0x004b1788</c>, the hex form of a numeric id.
///
/// Keys are *not* unique. The same spelling recurs across sections with different text - the patched
/// table has 50 such collisions, e.g. <c>MENU_TITLE</c> is "LOAD GAME" under <c>LoadMenu</c> and
/// "SAVE GAME" under <c>SaveMenu</c> - and a handful differ only by case
/// (<c>QUICKSAVE</c> "Quick Save -" vs <c>quicksave</c> "Quick Save"). So the lookup is exact first
/// and case-insensitive only as a fallback, and an unscoped key with genuinely different candidates
/// reports all of them rather than picking one: showing the wrong half of an ambiguous pair with no
/// hint that it was a guess is worse than showing both.
/// </remarks>
public static class OasisStringTable
{
    private const string RmlPath = @"languages\english\oasisstrings.rml";

    private static Func<string, byte[]?>? _source;
    private static Lazy<Table> _table = Defer();

    private sealed record Entry(string Section, string Value);

    /// <summary>Keys as written, and the same entries again qualified by their section - one parse,
    /// two indexes, because a reference may or may not name the section. Each key maps to every
    /// entry that claims it, since the table does not enforce uniqueness.</summary>
    private sealed record Table(
        Dictionary<string, List<Entry>> ByKey,
        Dictionary<string, List<Entry>> ByKeyIgnoringCase,
        Dictionary<string, string> BySectionAndKey)
    {
        public static Table Empty => new([], [], []);
    }

    /// <summary>Points the table at the merged filesystem. Cheap and non-blocking: nothing is read
    /// until something actually asks for a string.</summary>
    public static void UseSource(Func<string, byte[]?> source)
    {
        _source = source;
        _table = Defer();
    }

    /// <summary>How many distinct keys are loaded, or 0 if the table wasn't found or hasn't been
    /// read yet - for a status line, never as a "is it ready" check (asking forces the load).</summary>
    public static int Count => _table.IsValueCreated ? _table.Value.ByKey.Count : 0;

    /// <summary>
    /// The localised string <paramref name="text"/> names, or null if it names nothing - the ordinary
    /// case for a text widget holding literal content rather than a key. When an unscoped key matches
    /// several sections with different text, every candidate comes back tagged with its section.
    /// </summary>
    public static string? Resolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Table table = _table.Value;
        string key = text.Trim();

        // "Generic;LOADING" - the section qualifies the key, so that pairing is both tried first and
        // never ambiguous.
        int separator = key.LastIndexOf(';');
        if (separator > 0 && separator < key.Length - 1)
        {
            if (table.BySectionAndKey.TryGetValue(Qualify(key[..separator], key[(separator + 1)..]), out string? scoped))
            {
                return scoped;
            }
            key = key[(separator + 1)..];
        }

        if (Describe(table.ByKey, key) is { } exact)
        {
            return exact;
        }
        if (Describe(table.ByKeyIgnoringCase, key) is { } insensitive)
        {
            return insensitive;
        }

        // "0x004b1788" - the same id the table spells in decimal.
        return key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(key.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id)
                ? Describe(table.ByKey, id.ToString(CultureInfo.InvariantCulture))
                : null;
    }

    /// <summary>One candidate's text, or - when the key covers several genuinely different ones -
    /// all of them, each tagged with the section it came from.</summary>
    private static string? Describe(Dictionary<string, List<Entry>> index, string key)
    {
        if (!index.TryGetValue(key, out List<Entry>? entries) || entries.Count == 0)
        {
            return null;
        }

        Entry[] distinct = [.. entries
            .GroupBy(e => e.Value, StringComparer.Ordinal)
            .Select(g => g.First())];

        return distinct.Length == 1
            ? distinct[0].Value
            : string.Join("   ·   ", distinct.Select(e => $"\"{e.Value}\" [{e.Section}]"));
    }

    private static string Qualify(string section, string key) => $"{section.Trim()} {key.Trim()}";

    private static Lazy<Table> Defer()
        => new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>An empty table on any failure - a missing or unreadable string table means "nothing
    /// resolves", which is exactly how an unrecognised key already reads. Nothing in the editor
    /// depends on it being there.</summary>
    private static Table Build() => Index(LoadRoot());

    /// <summary>The winning layer's <c>oasisstrings.rml</c>, decoded - or null if no layer provides
    /// it, the read throws, or it isn't a well-formed <c>.rml</c>.</summary>
    private static XElement? LoadRoot()
    {
        try
        {
            byte[]? bytes = _source?.Invoke(RmlPath);
            return bytes is not null && RmlDocument.TryDeserialize(bytes, out XElement? root) ? root : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The decoded table is <c>&lt;stringtable&gt;</c> of <c>&lt;section name&gt;</c> of
    /// <c>&lt;string enum value&gt;</c>.</summary>
    private static Table Index(XElement? root)
    {
        if (root is null)
        {
            return Table.Empty;
        }

        var byKey = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        var byKeyIgnoringCase = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        var bySectionAndKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (XElement element in root.Descendants("string"))
        {
            if (element.Attribute("enum")?.Value is not { Length: > 0 } key)
            {
                continue;
            }

            string section = element.Parent?.Attribute("name")?.Value ?? string.Empty;
            var entry = new Entry(section, element.Attribute("value")?.Value ?? string.Empty);

            Add(byKey, key, entry);
            Add(byKeyIgnoringCase, key, entry);
            if (section.Length > 0)
            {
                bySectionAndKey[Qualify(section, key)] = entry.Value;
            }
        }
        return new Table(byKey, byKeyIgnoringCase, bySectionAndKey);

        static void Add(Dictionary<string, List<Entry>> index, string key, Entry entry)
        {
            if (!index.TryGetValue(key, out List<Entry>? entries))
            {
                index[key] = entries = [];
            }
            entries.Add(entry);
        }
    }
}
