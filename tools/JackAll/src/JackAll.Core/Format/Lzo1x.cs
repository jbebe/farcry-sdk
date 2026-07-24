namespace JackAll.Core.Format;

/// <summary>
/// LZO1X decompression — the scheme behind ~75% of the entries in worlds.dat and 98% of patch.dat.
/// </summary>
/// <remarks>
/// LZO ("Lempel-Ziv-Oberhumer") is Markus Oberhumer's family of byte-oriented compressors, tuned
/// for very fast decompression over compression ratio. "1X" names one specific member of that
/// family — LZO1, LZO1A/B/C/F, LZO1X, LZO1Y, LZO1Z and LZO2A are all distinct byte formats with
/// their own match-encoding rules, not compression-level knobs on one format, so decoding any other
/// member would need a different implementation entirely. Nothing in this repo's research/reverse
/// notes documents Ubisoft's own choice of LZO1X specifically — that label (and this decoder) came
/// from matching Gibbed.Dunia, the reference open-source Dunia toolset (see
/// research/knowledge_sources.md). What *is* verified directly against this game's data: the state
/// machine below matches miniLZO's <c>lzo1x_decompress_safe</c> labels one-for-one, and
/// <c>Lzo1xTests</c> round-trips it against every compressed entry in every shipped archive — so
/// whatever the exact provenance, the byte format it decodes is confirmed LZO1X-compatible. See
/// https://www.oberhumer.com/opensource/lzo/ (the algorithm's home; GPLv2+) for the miniLZO source
/// itself, the origin of the state labels below.
///
/// Managed on purpose. The alternative was P/Invoking the lzo1x DLL that ships with the old
/// toolchain, but that binary is 32-bit while this app is 64-bit, so it would have forced either a
/// 32-bit build or an out-of-process shim for what is ~150 lines of bit-twiddling. This is a
/// clean-room rewrite from the published bytestream format (not a translation of Oberhumer's GPL
/// source) — worth keeping that way, both to avoid pulling GPL code into this tree and because the
/// format itself, not any particular C implementation, is the actual contract with the game data.
///
/// There is no compressor here and there does not need to be: the builder copies existing
/// compressed entries through byte-for-byte and writes new ones uncompressed.
///
/// miniLZO's decoder is written with gotos that jump between scopes, which C# forbids, so the
/// control flow here is expressed as the state machine those labels actually describe. The states
/// map 1:1 onto the original's labels (top / first_literal_run / match / match_done / match_next),
/// which is the only reason this is readable at all — do not "simplify" it back into nested loops.
///
/// Correctness is not taken on faith: <c>Lzo1xTests</c> runs this over every compressed entry in
/// every shipped archive and checks each against the length the index independently claims.
/// </remarks>
public static class Lzo1x
{
    private enum State
    {
        Top,
        FirstLiteralRun,
        Match,
        MatchDone,
        MatchNext,
    }

    /// <summary>
    /// Decompresses <paramref name="input"/> into a buffer of exactly
    /// <paramref name="expectedSize"/> bytes (the size the .fat index recorded for the entry).
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int expectedSize)
    {
        var output = new byte[expectedSize];
        int written = Decompress(input, output);
        if (written != expectedSize)
        {
            throw new InvalidDataException(
                $"LZO stream decompressed to {written} bytes but the archive index says {expectedSize}.");
        }
        return output;
    }

    /// <summary>Returns the number of bytes written into <paramref name="output"/>.</summary>
    public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length == 0)
        {
            return 0;
        }

        int ip = 0; // input cursor
        int op = 0; // output cursor
        int t;
        State state;

        // Prologue: a first byte above 17 means the stream opens with a literal run, encoded
        // differently from every later one.
        if (input[ip] > 17)
        {
            t = input[ip++] - 17;
            if (t < 4)
            {
                state = State.MatchNext;
            }
            else
            {
                CopyLiterals(input, ref ip, output, ref op, t);
                state = State.FirstLiteralRun;
            }
        }
        else
        {
            t = 0;
            state = State.Top;
        }

        while (true)
        {
            switch (state)
            {
                case State.Top:
                    t = input[ip++];
                    if (t >= 16)
                    {
                        state = State.Match;
                        break;
                    }
                    if (t == 0)
                    {
                        // Each zero byte extends the run by 255; the terminator carries the rest.
                        while (input[ip] == 0)
                        {
                            t += 255;
                            ip++;
                        }
                        t += 15 + input[ip++];
                    }
                    CopyLiterals(input, ref ip, output, ref op, t + 3);
                    state = State.FirstLiteralRun;
                    break;

                case State.FirstLiteralRun:
                    t = input[ip++];
                    if (t >= 16)
                    {
                        state = State.Match;
                        break;
                    }
                    CopyMatch(output, ref op, back: 1 + 0x0800 + (t >> 2) + (input[ip++] << 2), count: 3);
                    state = State.MatchDone;
                    break;

                case State.Match:
                {
                    int back;
                    int length;

                    if (t >= 64)
                    {
                        back = 1 + ((t >> 2) & 7) + (input[ip++] << 3);
                        length = (t >> 5) - 1;
                    }
                    else if (t >= 32)
                    {
                        length = t & 31;
                        if (length == 0)
                        {
                            while (input[ip] == 0)
                            {
                                length += 255;
                                ip++;
                            }
                            length += 31 + input[ip++];
                        }
                        back = 1 + (ReadUInt16(input, ip) >> 2);
                        ip += 2;
                    }
                    else if (t >= 16)
                    {
                        // The only branch that can encode end-of-stream, as a zero distance.
                        back = (t & 8) << 11;
                        length = t & 7;
                        if (length == 0)
                        {
                            while (input[ip] == 0)
                            {
                                length += 255;
                                ip++;
                            }
                            length += 7 + input[ip++];
                        }
                        back += ReadUInt16(input, ip) >> 2;
                        ip += 2;
                        if (back == 0)
                        {
                            return op;
                        }
                        back += 0x4000;
                    }
                    else
                    {
                        CopyMatch(output, ref op, back: 1 + (t >> 2) + (input[ip++] << 2), count: 2);
                        state = State.MatchDone;
                        break;
                    }

                    CopyMatch(output, ref op, back, length + 2);
                    state = State.MatchDone;
                    break;
                }

                case State.MatchDone:
                    // The low 2 bits of the last distance byte carry a 0-3 byte literal run.
                    t = input[ip - 2] & 3;
                    state = t == 0 ? State.Top : State.MatchNext;
                    break;

                case State.MatchNext:
                    CopyLiterals(input, ref ip, output, ref op, t);
                    t = input[ip++];
                    state = State.Match;
                    break;
            }
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> input, int offset)
        => (ushort)(input[offset] | (input[offset + 1] << 8));

    private static void CopyLiterals(
        ReadOnlySpan<byte> input, ref int ip, Span<byte> output, ref int op, int count)
    {
        input.Slice(ip, count).CopyTo(output[op..]);
        ip += count;
        op += count;
    }

    /// <summary>
    /// Copies <paramref name="count"/> bytes from <paramref name="back"/> bytes earlier in the output.
    /// </summary>
    /// <remarks>
    /// Two genuinely different cases, not just a style choice: when <paramref name="back"/> &gt;=
    /// <paramref name="count"/>, the source range ends at or before <paramref name="op"/> - it can't
    /// overlap the destination at all, so a single bulk <see cref="Span{T}.CopyTo"/> (a vectorized
    /// <see cref="Buffer.Memmove"/>, not a per-byte bounds-checked loop) is both correct and, per
    /// perf.txt, the dominant cost in this whole decoder before this fast path existed - one profiled
    /// trace spent 10s+ of self time purely in <c>Span&lt;T&gt;.get_Item</c> inside this loop.
    ///
    /// When <paramref name="back"/> &lt; <paramref name="count"/>, the ranges genuinely overlap - this
    /// is how LZO encodes a repeating run (e.g. <c>back: 1</c> repeats the immediately preceding byte
    /// <paramref name="count"/> times). A bulk copy is wrong there, not just unsafe: <c>Memmove</c>'s
    /// overlap handling preserves the *original* bytes' relative order (like shifting a buffer), which
    /// is a different result than each new byte re-reading the one just written a moment ago. That case
    /// must stay a forward byte-at-a-time loop, deliberately - do not "simplify" it into a bulk copy.
    /// </remarks>
    private static void CopyMatch(Span<byte> output, ref int op, int back, int count)
    {
        int src = op - back;
        if (src < 0)
        {
            throw new InvalidDataException(
                "Corrupt LZO stream: back-reference points before the start of the output.");
        }

        if (back >= count)
        {
            output.Slice(src, count).CopyTo(output.Slice(op, count));
            op += count;
            return;
        }

        for (int i = 0; i < count; i++)
        {
            output[op++] = output[src++];
        }
    }
}
