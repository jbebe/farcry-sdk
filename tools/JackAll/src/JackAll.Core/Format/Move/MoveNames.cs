using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace JackAll.Core.Format.Move;

/// <summary>
/// What the names behind a MOVE graph's hashes are, recovered from the authoring twin.
/// </summary>
/// <remarks>
/// A loadable graph carries no names at all - a state is a bare <c>CPathID</c>, and
/// <c>GetValueNameFromID</c> in the shipped engine returns <c>"INVALID VALUENAME"</c> for everything,
/// because the names were compiled away. They survive only in <c>movemgrnamed.bin</c>, which no
/// engine will load and which is decoded to about 2% of its length.
///
/// None of that matters here, because this does not decode it. It takes every length-prefixed ASCII
/// string out of the twin, hashes each one, and keeps the ones that match a hash the loadable graph
/// actually uses. **The match is the proof**: a string that hashes to a hash the graph keys on is
/// that name, and a wrong string cannot pass at a rate better than 2^-32. The two files never have to
/// agree structurally, so an undecoded authoring format costs nothing.
///
/// Measured: **100% of `movemgr.bin`'s 1,700 state names**, and every package, anchor part and model
/// part name too. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveNames
{
    private readonly Dictionary<uint, string> _byHash;

    private MoveNames(Dictionary<uint, string> byHash) => _byHash = byHash;

    public static MoveNames Empty { get; } = new([]);

    public int Count => _byHash.Count;

    public string? Of(uint hash) => _byHash.GetValueOrDefault(hash);

    public IEnumerable<KeyValuePair<uint, string>> All => _byHash;

    /// <summary>
    /// A <c>CPathID</c>: plain CRC-32 of the lowercased name.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="NameHash.Compute"/>, which also folds <c>/</c> to <c>\</c> because
    /// it hashes archive paths. A state name may contain a forward slash
    /// (<c>Pawn_Generic_Aim_First/Stand</c>), and folding it changes the hash.
    /// </remarks>
    public static uint HashOf(string name)
        => Crc32.HashToUInt32(Encoding.ASCII.GetBytes(name.ToLowerInvariant()));

    /// <summary>Every hash a graph keys something on, which is what a candidate name must match.</summary>
    public static HashSet<uint> HashesIn(MoveFile file)
    {
        HashSet<uint> wanted = [];
        foreach (MoveObject obj in file.Objects)
        {
            foreach (MoveOp op in obj.Ops)
            {
                // m_animNameHash is deliberately absent: a clip is the CPathID of a game path, which
                // the twin does not spell out and the hashlist already resolves. Measured 0% here.
                if (op.Name is "m_stateNameHash" or "aliasID" or "m_package" or "m_anchorPartName"
                    or "m_poseNameHash" or "m_iModelHashNamePartID" or "m_iHandleHash"
                    && op.Number != 0xFFFFFFFF)
                {
                    wanted.Add(op.Number);
                }
            }
        }

        return wanted;
    }

    /// <summary>
    /// Recovers the names in <paramref name="namedTwin"/> for the hashes in
    /// <paramref name="wanted"/>. Anything that does not hash to a wanted value is discarded, so
    /// there is nothing to trust and no way for a stray string to get in.
    /// </summary>
    public static MoveNames Harvest(byte[] namedTwin, IReadOnlySet<uint> wanted)
    {
        Dictionary<uint, string> found = [];
        for (int i = 0; i + 4 < namedTwin.Length; i++)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(namedTwin.AsSpan(i));
            if (length is 0 or > 256 || i + 4 + length > namedTwin.Length)
            {
                continue;
            }

            ReadOnlySpan<byte> text = namedTwin.AsSpan(i + 4, (int)length);
            if (!IsPrintable(text))
            {
                continue;
            }

            string candidate = Encoding.ASCII.GetString(text);
            uint hash = HashOf(candidate);
            if (wanted.Contains(hash))
            {
                found.TryAdd(hash, candidate);
            }
        }

        return new MoveNames(found);
    }

    private static bool IsPrintable(ReadOnlySpan<byte> text)
    {
        foreach (byte b in text)
        {
            if (b is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    public MoveNames MergedWith(MoveNames other)
    {
        Dictionary<uint, string> merged = new(_byHash);
        foreach ((uint hash, string name) in other._byHash)
        {
            merged.TryAdd(hash, name);
        }

        return new MoveNames(merged);
    }

    /// <summary>The bundled form: one <c>hash TAB name</c> row per line, sorted, with a header.</summary>
    public string ToTsv()
    {
        StringBuilder text = new();
        text.AppendLine("# MOVE graph names, keyed by CPathID (CRC-32 of the lowercased name).");
        text.AppendLine("# Generated by `jackall-cli move names`; do not hand-edit.");
        text.AppendLine("# Recovered from the *named.bin authoring twins by hashing every string they");
        text.AppendLine("# hold and keeping the ones a loadable graph actually keys on - so every row");
        text.AppendLine("# here is self-proving, and the twins' own format never had to be decoded.");
        foreach ((uint hash, string name) in _byHash.OrderBy(p => p.Key))
        {
            text.Append(hash.ToString("X8")).Append('\t').AppendLine(name);
        }

        return text.ToString();
    }

    public static MoveNames Load(string path)
    {
        Dictionary<uint, string> byHash = [];
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab == 8 && uint.TryParse(
                    line.AsSpan(0, 8), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint hash))
            {
                byHash[hash] = line[(tab + 1)..].TrimEnd();
            }
        }

        return new MoveNames(byHash);
    }
}
