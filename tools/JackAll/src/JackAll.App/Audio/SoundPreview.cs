using System.IO;
using JackAll.Core.Vfs;
using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;
using JackAll.Tools.Spk;

namespace JackAll.App.Audio;

/// <summary>Decodes game audio into a temp .wav for <c>AudioPreviewPanel</c>. The caller owns the
/// returned file and removes it with <see cref="TryDelete"/>.</summary>
public static class SoundPreview
{
    /// <summary>The rate for an IMA-ADPCM record with no descriptor: the commonest one in a real install.</summary>
    public const int FallbackSampleRateHz = 32000;

    private const int MaxHops = 8;

    public static int SampleRateOf(SpkPackage package, SpkRecord record) =>
        package.TryGetFlatCopySampleRate(record) ?? FallbackSampleRateHz;

    public static async Task<string> OggToTempWavAsync(byte[] ogg)
    {
        string tempOgg = TempPath(".ogg");
        string wav = Path.ChangeExtension(tempOgg, ".wav");
        try
        {
            await File.WriteAllBytesAsync(tempOgg, ogg);
            await FfmpegAudio.TranscodeToWavAsync(tempOgg, wav);
            return wav;
        }
        finally
        {
            TryDelete(tempOgg);
        }
    }

    /// <summary>A `FlatCopy` record's stream, which is either a complete Ogg Vorbis file or IMA-ADPCM.</summary>
    public static async Task<string> RecordToTempWavAsync(SpkPackage package, SpkRecord record)
    {
        if (record.FlatCopyAudioStream is { } stream && SbaoAudio.TryReadVorbisId(stream) is not null)
        {
            return await OggToTempWavAsync(stream);
        }

        string wav = TempPath(".wav");
        await File.WriteAllBytesAsync(wav, ImaAdpcmToWav(package, record, out _));
        return wav;
    }

    public static byte[] ImaAdpcmToWav(SpkPackage package, SpkRecord record, out int sampleRate)
    {
        byte[] stream = record.FlatCopyAudioStream
            ?? throw new InvalidOperationException("This record holds no audio.");
        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);
        sampleRate = SampleRateOf(package, record);
        return WavAudio.Write(decoded.Samples, decoded.Channels, sampleRate);
    }

    /// <summary>The first audio a sound ID plays, following each record's <see cref="SpkRecord.Links"/>
    /// within its bank first, then through <paramref name="resolve"/>.</summary>
    /// <param name="bank">Where to look for <paramref name="soundId"/> first, when it names a record
    /// rather than a file.</param>
    public static async Task<string> SoundIdToTempWavAsync(
        uint soundId, Func<uint, VfsFile?> resolve, Func<VfsFile, byte[]> read, SpkPackage? bank = null)
    {
        var pending = new Queue<(uint Id, SpkPackage? Bank)>([(soundId, bank)]);
        var seen = new HashSet<uint> { soundId };
        uint? missing = null;

        for (int hops = 0; pending.Count > 0 && hops < MaxHops; hops++)
        {
            (uint id, SpkPackage? within) = pending.Dequeue();
            SpkPackage package;
            IReadOnlyList<SpkRecord> records;
            if (within?.Records.FirstOrDefault(r => r.Id == id) is { } record)
            {
                package = within;
                records = [record];
            }
            else if (resolve(id) is { } file)
            {
                byte[] bytes = read(file);
                if (file.Path.EndsWith(".sbao", StringComparison.OrdinalIgnoreCase))
                {
                    return await OggToTempWavAsync(SbaoAudio.Split(bytes).Ogg);
                }
                package = SpkPackage.Parse(bytes);
                records = package.Records;
            }
            else
            {
                missing ??= id;
                continue;
            }

            if (records.FirstOrDefault(r => r.FlatCopyAudioStream is not null) is { } audio)
            {
                return await RecordToTempWavAsync(package, audio);
            }
            foreach (uint link in records.SelectMany(r => r.Links).Where(l => l != 0 && seen.Add(l)))
            {
                pending.Enqueue((link, package));
            }
        }

        throw new InvalidOperationException(missing is not { } gap
            ? $"0x{soundId:x8} leads to no audio."
            : $"0x{soundId:x8} plays 0x{gap:x8}, which isn't in the loaded game files.");
    }

    public static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"jackall_audio_{Guid.NewGuid():N}{extension}");

    public static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of our own temp file - a lingering one isn't worth surfacing.
        }
    }
}
