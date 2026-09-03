using System.Globalization;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// The id every splitting container stages a fragment under: <c>&lt;label&gt;.&lt;number&gt;.xml</c>,
/// e.g. <c>dragunov.3882209901.xml</c>, or a bare <c>3882209901.xml</c> when there is no name to
/// read it by.
/// </summary>
/// <remarks>
/// The number binds and the label is decoration - the same cosmetic-name / authoritative-number
/// shape a placed entity's fragment uses (<c>Guard_12.2058514756624450165.xml</c>). That is what
/// makes every spelling of one item compare equal under <see cref="FcbFragments.IdComparer"/> with
/// no special case: whoever writes the id may know a name the reader does not, and vice versa, and
/// they still land on one entry. Decimal precisely because that comparer keys on a *numeric* tail.
///
/// The label has to be a flat leaf. <see cref="FcbFragments.Canonicalize"/> strips a cosmetic prefix
/// only from the last path segment and keeps the directory, so a label spelled as a nested path
/// (<c>graphics\weapons\…\dragunov.xbg.3882209901.xml</c>) would canonicalize to
/// <c>graphics\weapons\…\3882209901.xml</c> and stop matching the bare form - which is exactly the
/// mismatch this scheme exists to avoid.
/// </remarks>
public static class FragmentId
{
    private const string Extension = ".xml";

    public static string Of(uint number, string? label = null)
    {
        string leaf = Sanitize(label);
        return leaf.Length == 0 ? $"{number}{Extension}" : $"{leaf}.{number}{Extension}";
    }

    /// <summary>The number an id names, read through the same canonicalization
    /// <see cref="FcbFragments.IdComparer"/> keys on, so two ids that comparer calls equal resolve
    /// here too. Null when the id names nothing.</summary>
    public static uint? NumberOf(string fragmentId)
    {
        if (!fragmentId.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string stem = FcbFragments.Canonicalize(fragmentId)[..^Extension.Length];
        if (stem.Length == 0)
        {
            return null;
        }

        // Canonicalization has already reduced a labelled id to its number. Anything left that is not
        // numeric names the item outright, which still resolves - it just cannot compare equal to the
        // labelled form, so tooling never writes one.
        return uint.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out uint number)
            ? number
            : NameHash.Compute(stem);
    }

    /// <summary>The label part of an id: a bare filename, with anything a path or a filesystem would
    /// object to reduced to an underscore. Empty when there is nothing usable to read by.</summary>
    private static string Sanitize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> leaf = label.AsSpan(label.AsSpan().LastIndexOfAny('\\', '/') + 1).Trim();
        StringBuilder text = new(leaf.Length);
        foreach (char c in leaf)
        {
            text.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }

        return text.ToString();
    }
}
