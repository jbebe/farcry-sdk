using System.Globalization;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Sav;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands;

/// <summary>
/// Measures how much of a savegame's PersistenceDB tree resolves against a reverse hash -> string
/// dictionary harvested from String-typed values in a directory of already-resolved <c>.fcb</c> files
/// (entitylibrary, by default) - a save's own type/value hashes never resolve against
/// <c>binary_classes.xml</c> directly (see reverse/dunia/savegame_format.md), but many of them turn out
/// to be the CRC32 of the exact same names a sibling .fcb still spells out in the clear.
/// </summary>
public sealed class SavegameReverseCommand : Command<SavegameReverseCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<SAVE_PATH>")]
        public string SavePath { get; init; } = string.Empty;

        [CommandArgument(1, "<CORPUS_DIR>")]
        public string CorpusDir { get; init; } = string.Empty;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string classesPath = Path.Combine(AppContext.BaseDirectory, ".fcbclasses");
        FcbClassDefinitions definitions = File.Exists(classesPath)
            ? FcbClassDefinitions.Load(classesPath)
            : FcbClassDefinitions.Empty;

        AnsiConsole.MarkupLine($"Harvesting string corpus from [green]{settings.CorpusDir.EscapeMarkup()}[/]...");
        FcbStringCorpus corpus = FcbStringCorpus.BuildFromDirectory(settings.CorpusDir, definitions);
        AnsiConsole.MarkupLine(
            $"  {corpus.FilesLoaded} .fcb file(s), {corpus.ByHash.Count} distinct string(s), " +
            $"{corpus.Ambiguous.Count} hash collision(s).");

        AnsiConsole.MarkupLine($"Loading save [green]{settings.SavePath.EscapeMarkup()}[/]...");
        SaveGameInfo info = SaveGameDocument.Read(settings.SavePath);
        FcbObject root = SaveGameDocument.ReadFcbRoot(info);

        var typeHashOccurrences = new Dictionary<uint, int>();
        var valueHashOccurrences = new Dictionary<uint, int>();
        Walk(root, typeHashOccurrences, valueHashOccurrences);

        AnsiConsole.WriteLine();
        ReportAxis("Object type hashes", typeHashOccurrences, corpus.ByHash);
        ReportAxis("4-byte value payloads (candidate Hash refs)", valueHashOccurrences, corpus.ByHash);

        return 0;
    }

    /// <summary>Tallies two independent axes in one pass: how often each object's own type hash
    /// appears, and how often each distinct 4-byte value payload appears (a value this size is a
    /// candidate Hash-typed reference - the save never carries a declared type to confirm that, so
    /// every 4-byte value is a candidate, resolved or not).</summary>
    private static void Walk(
        FcbObject obj, Dictionary<uint, int> typeHashOccurrences, Dictionary<uint, int> valueHashOccurrences)
    {
        typeHashOccurrences[obj.TypeHash] = typeHashOccurrences.GetValueOrDefault(obj.TypeHash) + 1;

        foreach (byte[] value in obj.Values.Values)
        {
            if (value.Length != 4)
            {
                continue;
            }

            uint asHash = BitConverter.ToUInt32(value, 0);
            valueHashOccurrences[asHash] = valueHashOccurrences.GetValueOrDefault(asHash) + 1;
        }

        foreach (FcbObject child in obj.Children)
        {
            Walk(child, typeHashOccurrences, valueHashOccurrences);
        }
    }

    private static void ReportAxis(string label, Dictionary<uint, int> occurrences, IReadOnlyDictionary<uint, string> corpus)
    {
        int distinctResolved = 0;
        long occurrencesResolved = 0;
        long totalOccurrences = 0;

        foreach ((uint hash, int count) in occurrences)
        {
            totalOccurrences += count;
            if (corpus.ContainsKey(hash))
            {
                distinctResolved++;
                occurrencesResolved += count;
            }
        }

        double distinctPct = occurrences.Count == 0 ? 0 : 100.0 * distinctResolved / occurrences.Count;
        double occPct = totalOccurrences == 0 ? 0 : 100.0 * occurrencesResolved / totalOccurrences;

        AnsiConsole.MarkupLine($"[bold]{label.EscapeMarkup()}[/]:");
        AnsiConsole.MarkupLine(
            $"  {distinctResolved}/{occurrences.Count} distinct resolved ({distinctPct.ToString("F1", CultureInfo.InvariantCulture)}%)");
        AnsiConsole.MarkupLine(
            $"  {occurrencesResolved}/{totalOccurrences} occurrences resolved ({occPct.ToString("F1", CultureInfo.InvariantCulture)}%)");
    }
}
