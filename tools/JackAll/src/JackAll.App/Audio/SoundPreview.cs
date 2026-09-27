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
    private const int MaxHops = 8;

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

    /// <summary>An audio record's stream, which is either a complete Ogg Vorbis file or IMA-ADPCM.</summary>
    public static async Task<string> AudioToTempWavAsync(SpkBankRecord audio)
    {
        if (SpkBank.DescribeAudio(audio) is { Ogg: true })
        {
            return await OggToTempWavAsync(audio.Data);
        }

        string wav = TempPath(".wav");
        await File.WriteAllBytesAsync(wav, ImaAdpcmToWav(audio));
        return wav;
    }

    public static byte[] ImaAdpcmToWav(SpkBankRecord audio)
    {
        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio.Data);
        return WavAudio.Write(decoded.Samples, decoded.Channels, audio.SampleRate ?? SpkBank.FallbackSampleRate);
    }

    /// <summary>The audio a sound ID plays, followed within its bank first, then through
    /// <paramref name="resolve"/>; a random container plays a choice picked by its weights.</summary>
    /// <param name="bank">Where to look for <paramref name="soundId"/> first, when it names a record
    /// rather than a file.</param>
    public static async Task<string> SoundIdToTempWavAsync(
        uint soundId, Func<uint, VfsFile?> resolve, Func<VfsFile, byte[]> read, SpkBank? bank = null)
    {
        var pending = new Queue<(uint Id, SpkBank? Bank)>([(soundId, bank)]);
        var seen = new HashSet<uint> { soundId };
        uint? missing = null;

        for (int hops = 0; pending.Count > 0 && hops < MaxHops; hops++)
        {
            (uint id, SpkBank? within) = pending.Dequeue();
            SpkBankRecord? start = within?.Find(id);
            if (start is null && resolve(id) is { } file)
            {
                byte[] bytes = read(file);
                if (file.Path.EndsWith(".sbao", StringComparison.OrdinalIgnoreCase))
                {
                    return await OggToTempWavAsync(SbaoAudio.Split(bytes).Ogg);
                }
                within = SpkBank.Parse(bytes);
                start = within.Find(id) ?? within.Records.FirstOrDefault(r => r.IsEvent) ?? within.Records.FirstOrDefault();
            }
            if (start is null || within is null)
            {
                missing ??= id;
                continue;
            }

            var elsewhere = new List<uint>();
            if (within.PickAudio(start, elsewhere) is { } audio)
            {
                return await AudioToTempWavAsync(audio);
            }
            foreach (uint link in elsewhere.Where(seen.Add))
            {
                pending.Enqueue((link, within));
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
