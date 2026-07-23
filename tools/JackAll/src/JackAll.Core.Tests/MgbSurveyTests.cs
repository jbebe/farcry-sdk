using System.Text.RegularExpressions;
using JackAll.Core.Format;
using Xunit.Abstractions;

namespace JackAll.Core.Tests;

/// <summary>Throwaway diagnostic over a folder of real .mgb files - not part of the real suite.</summary>
public class MgbSurveyTests(ITestOutputHelper output)
{
    [Fact]
    public void Survey()
    {
        string dir = @"C:\Projects\FarCry2\tmp\mgbs";
        if (!Directory.Exists(dir)) { output.WriteLine("dir not found"); return; }

        var crcCounts = new Dictionary<uint, int>();
        int fullyParsed = 0;
        int stoppedOnClass = 0;
        int otherFailure = 0;

        foreach (string path in Directory.GetFiles(dir, "*.mgb").OrderBy(p => p))
        {
            string name = Path.GetFileName(path);
            byte[] content = File.ReadAllBytes(path);
            try
            {
                MgbHeader header = MgbHeader.Decode(content);
                MgbNode body = MgbBody.ParsePackage(content, header);
                string? stopped = body.Fields.FirstOrDefault(f => f.Label == "StoppedDecoding").Value;
                if (stopped is null)
                {
                    fullyParsed++;
                    output.WriteLine($"{name}: FULLY PARSED ({content.Length} bytes)");
                }
                else
                {
                    stoppedOnClass++;
                    Match m = Regex.Match(stopped, "crc32=0x([0-9A-Fa-f]+)");
                    uint crc = m.Success ? Convert.ToUInt32(m.Groups[1].Value, 16) : 0;
                    if (crc != 0) crcCounts[crc] = crcCounts.GetValueOrDefault(crc) + 1;
                    output.WriteLine($"{name}: stopped - {stopped}");
                }
            }
            catch (Exception ex)
            {
                otherFailure++;
                output.WriteLine($"{name}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
        }

        output.WriteLine("");
        output.WriteLine($"fullyParsed={fullyParsed} stoppedOnClass={stoppedOnClass} otherFailure={otherFailure}");
        output.WriteLine("Unresolved class frequency (crc32 -> file count):");
        foreach (var kv in crcCounts.OrderByDescending(kv => kv.Value))
        {
            output.WriteLine($"  0x{kv.Key:X8}: {kv.Value} files");
        }
    }
}
