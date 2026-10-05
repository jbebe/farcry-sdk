using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;

namespace JackAll.Core.Legacy;

/// <summary>One differing run of lines, at its 1-based line in the base text.</summary>
public sealed record TextHunk(int Line, string Old, string New);

/// <summary>
/// Line hunks between two texts, and the base text with only some of them applied. Lines keep their
/// own terminators, so taking every hunk reproduces the mod's text byte for byte.
/// </summary>
public static class TextHunks
{
    public static List<TextHunk> Diff(string vanilla, string mod)
    {
        DiffResult result = Compare(vanilla, mod);
        return [.. result.DiffBlocks.Select(block => new TextHunk(
            block.DeleteStartA + 1,
            string.Concat(result.PiecesOld.Skip(block.DeleteStartA).Take(block.DeleteCountA)),
            string.Concat(result.PiecesNew.Skip(block.InsertStartB).Take(block.InsertCountB))))];
    }

    /// <summary><paramref name="vanilla"/> with the hunks whose line <paramref name="take"/> accepts.</summary>
    public static string Merge(string vanilla, string mod, Func<int, bool> take)
    {
        DiffResult result = Compare(vanilla, mod);
        var text = new System.Text.StringBuilder();
        int next = 0;
        foreach (DiffBlock block in result.DiffBlocks)
        {
            text.Append(string.Concat(result.PiecesOld.Skip(next).Take(block.DeleteStartA - next)));
            text.Append(take(block.DeleteStartA + 1)
                ? string.Concat(result.PiecesNew.Skip(block.InsertStartB).Take(block.InsertCountB))
                : string.Concat(result.PiecesOld.Skip(block.DeleteStartA).Take(block.DeleteCountA)));
            next = block.DeleteStartA + block.DeleteCountA;
        }

        text.Append(string.Concat(result.PiecesOld.Skip(next)));
        return text.ToString();
    }

    private static DiffResult Compare(string vanilla, string mod)
        => Differ.Instance.CreateDiffs(vanilla, mod, ignoreWhiteSpace: false, ignoreCase: false, TerminatedLines.Instance);

    /// <summary>Splits after each line feed, keeping it, so the pieces concatenate back exactly.</summary>
    private sealed class TerminatedLines : IChunker
    {
        public static TerminatedLines Instance { get; } = new();

        public IReadOnlyList<string> Chunk(string text)
        {
            List<string> lines = [];
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines.Add(text[start..(i + 1)]);
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                lines.Add(text[start..]);
            }

            return lines;
        }
    }
}
