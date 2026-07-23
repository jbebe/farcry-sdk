using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace JackAll.App.FileHandlers.Text;

/// <summary>
/// Turns a whole-file diff into the compact "just the changes" view a modder actually wants: every
/// changed line plus a few lines of surrounding context, with everything else collapsed behind a
/// single marker line - so opening an overridden text file immediately shows what changed instead of
/// burying it in an otherwise-identical few hundred lines. The full file is still one Export… away, or
/// a normal open from the workspace folder, for anyone who wants it.
/// </summary>
public static class DiffTextBuilder
{
    private const int ContextLines = 3;

    public static IReadOnlyList<DiffLine> BuildTrimmedDiff(string originalText, string currentText)
    {
        List<DiffPiece> lines = InlineDiffBuilder.Diff(originalText, currentText).Lines;

        var keep = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Type == ChangeType.Unchanged)
            {
                continue;
            }
            for (int j = Math.Max(0, i - ContextLines); j <= Math.Min(lines.Count - 1, i + ContextLines); j++)
            {
                keep[j] = true;
            }
        }

        var result = new List<DiffLine>();
        int gapStart = -1;
        for (int i = 0; i <= lines.Count; i++)
        {
            bool kept = i < lines.Count && keep[i];
            if (!kept)
            {
                if (gapStart < 0) gapStart = i;
                continue;
            }

            if (gapStart >= 0)
            {
                AppendGap(result, gapStart, i);
                gapStart = -1;
            }

            DiffPiece line = lines[i];
            DiffLineKind kind = line.Type switch
            {
                ChangeType.Inserted => DiffLineKind.Added,
                ChangeType.Deleted => DiffLineKind.Removed,
                _ => DiffLineKind.Unchanged,
            };
            result.Add(new DiffLine(line.Text ?? string.Empty, kind));
        }
        if (gapStart >= 0)
        {
            AppendGap(result, gapStart, lines.Count);
        }

        return result;
    }

    private static void AppendGap(List<DiffLine> result, int start, int end)
    {
        int count = end - start;
        if (count <= 0) return;

        string label = count == 1 ? "1 unchanged line" : $"{count:N0} unchanged lines";
        result.Add(new DiffLine($"⋯ {label} ⋯", DiffLineKind.Gap));
    }
}
