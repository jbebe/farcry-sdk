using JackAll.Core;

namespace JackAll.Tests;

public sealed class VanillaHashesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public VanillaHashesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Steam and GOG ship different clean patches; either one is clean, anything else isn't.</summary>
    [Fact]
    public void A_path_listed_twice_accepts_either_release()
    {
        string steam = Path.Combine(_dir, "steam.dat"), gog = Path.Combine(_dir, "gog.dat");
        File.WriteAllText(steam, "steam");
        File.WriteAllText(gog, "gog");
        string list = Path.Combine(_dir, "hashes.ini");
        File.WriteAllLines(list, [
            "[hashes]",
            $"patch.dat = {VanillaHashes.ComputeHash(steam)}",
            $"patch.dat = {VanillaHashes.ComputeHash(gog)}",
        ]);
        VanillaHashes hashes = VanillaHashes.Load(list);

        foreach ((string content, bool clean) in new[] { ("steam", true), ("gog", true), ("modded", false) })
        {
            File.WriteAllText(Path.Combine(_dir, "patch.dat"), content);
            Assert.Equal(clean, hashes.FindMismatches(_dir, ["patch.dat"]).Count == 0);
        }
    }
}
