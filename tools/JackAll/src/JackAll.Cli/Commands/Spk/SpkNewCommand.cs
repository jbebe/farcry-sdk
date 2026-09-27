using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Spk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Scaffolds the XML for a new bank: an event playing one clip, or a random pick of several, with
/// the audio copied in beside it. <c>spk encode</c> builds it.
/// </summary>
/// <remarks>Ids run on from the event's: the random container, then a sample and its audio per clip.</remarks>
public sealed class SpkNewCommand : CliCommand<SpkNewCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<event-id>")]
        [Description("The id the game plays, e.g. 0x00fc0200. The bank is named after it.")]
        public string EventId { get; init; } = null!;

        [CommandArgument(1, "<audio-files>")]
        [Description("One .wav (16-bit PCM, encoded to IMA-ADPCM) or .ogg per variation.")]
        public string[] AudioFiles { get; init; } = [];

        [CommandOption("--rolloff <id>")]
        [Description("The rolloff curve that fades it with distance (default: --like's, else none: unpositioned).")]
        public string? Rolloff { get; init; }

        [CommandOption("--gain <dB>")]
        [Description("Each sample's gain in dB (default 0).")]
        public double Gain { get; init; }

        [CommandOption("--loop")]
        [Description("The samples loop.")]
        public bool Loop { get; init; }

        [CommandOption("--like <file.spk>")]
        [Description("A retail bank whose event and sample to copy the unidentified words from.")]
        public string? Like { get; init; }

        [CommandOption("-o|--out <dir>")]
        [Description("Output folder (default: a folder named after the event, in the current directory).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        if (settings.AudioFiles.Length == 0)
        {
            throw new InvalidDataException("Give at least one audio file.");
        }

        uint eventId = SpkFormat.ParseRecordId(settings.EventId);
        string dir = settings.Out ?? $"{eventId:x8}";
        CliIO.EnsureDirectory(dir);
        (SpkBankRecord? likePlay, SpkBankRecord? likeSample) = Template(settings.Like);

        var bank = new SpkBank { Preamble = [eventId] };
        uint next = eventId + 1;
        uint? random = settings.AudioFiles.Length > 1 ? next++ : null;
        var samples = new List<SpkBankRecord>();
        foreach (string source in settings.AudioFiles)
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not (".wav" or ".ogg"))
            {
                throw new InvalidDataException($"{source}: give a .wav or an .ogg.");
            }

            uint sampleId = next++;
            uint audioId = next++;
            string file = $"{audioId:x8}{extension}";
            CliIO.WriteOutput(Path.Combine(dir, file), CliIO.ReadInput(source));
            bank.Records.Add(new SpkBankRecord { Id = audioId, Type = SpkRecordType.FlatCopy, File = file });

            uint[] words = Words(SpkLayout.Sample, likeSample);
            words[SpkLayout.SampleGain] = SpkLayout.ToQ16(settings.Gain);
            words[SpkLayout.SampleAudio] = audioId;
            words[SpkLayout.SampleLoop] = settings.Loop ? 1u : 0u;
            samples.Add(new SpkBankRecord
            {
                Id = sampleId, Type = SpkRecordType.TransformedFixed128, Kind = (uint)SpkResourceKind.Sample, Words = words,
            });
        }
        bank.Records.AddRange(samples);

        if (random is { } randomId)
        {
            bank.Records.Add(new SpkBankRecord
            {
                Id = randomId, Type = SpkRecordType.TransformedFixed128, Kind = (uint)SpkResourceKind.Random,
                Words = SpkLayout.Random.NewWords(),
                Entries = [.. samples.Select(s => new SpkEntry(s.Id, SpkLayout.One / (uint)samples.Count))],
            });
        }

        uint[] play = Words(SpkLayout.Play, likePlay);
        play[2] = random ?? samples[0].Id;
        play[7] = settings.Rolloff is { } rolloff ? SpkFormat.ParseRecordId(rolloff) : likePlay?.Word(7) ?? SpkLayout.NoId;
        bank.Records.Add(new SpkBankRecord
        {
            Id = eventId, Type = SpkRecordType.SimpleFixed68, Kind = (uint)SpkEventType.Leaf, Words = play,
        });

        string xmlPath = Path.Combine(dir, $"{eventId:x8}.xml");
        CliIO.WriteOutput(xmlPath, SpkBankXml.ToXml(bank, (_, _) => { }));
        CliIO.ReportWrote(xmlPath);
        AnsiConsole.MarkupLine($"  build it with: jackall-cli spk encode {xmlPath.EscapeMarkup()}");
        return 0;
    }

    /// <summary>A layout's defaults, with a template record's unnamed words over them.</summary>
    private static uint[] Words(SpkLayout layout, SpkBankRecord? like)
    {
        uint[] words = layout.NewWords();
        for (int i = 0; like is not null && i < words.Length; i++)
        {
            if (!layout.IsNamed(i))
            {
                words[i] = like.Word(i);
            }
        }
        return words;
    }

    /// <summary>The template bank's event (the one it is named after, else its first) and the sample it plays.</summary>
    private static (SpkBankRecord? Play, SpkBankRecord? Sample) Template(string? path)
    {
        if (path is null)
        {
            return (null, null);
        }

        SpkBank bank = SpkBank.Parse(CliIO.ReadInput(path));
        SpkBankRecord? play = (SpkFormat.BankId(path) is { } id ? bank.Find(id) : null) is { Layout: var layout } named
            && layout == SpkLayout.Play ? named : bank.Records.Find(r => r.Layout == SpkLayout.Play);
        SpkBankRecord? sample = bank.Find(play?.Word(2) ?? 0) is { } sound && sound.Layout == SpkLayout.Sample
            ? sound : bank.Records.Find(r => r.Layout == SpkLayout.Sample);
        if (play is null || sample is null)
        {
            throw new InvalidDataException($"{path} holds no Play event and Sample to copy.");
        }
        return (play, sample);
    }
}
