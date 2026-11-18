using System.Buffers.Binary;
using JackAll.Core.Mods;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

public class SpkIdClashesTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [Fact]
    public void A_record_another_bank_holds_differently_clashes()
    {
        if (Fixture.Read(SpkBankTests.PassBy) is not { } bytes) return;

        FolderModLayer first = Layer("first", "00448bd2", bytes);
        FolderModLayer second = Layer("second", "00fd9999", WithFirstRecordEdited(bytes));

        SpkIdClash clash = Assert.Single(SpkIdClashes.Find([first, second]));
        Assert.Same(first, clash.First.Layer);
        Assert.Same(second, clash.Second.Layer);
        Assert.Equal([SpkPackage.Parse(bytes).Records[0].Id], clash.Ids);
    }

    [Theory]
    // Another bank with the same records, as retail's copies of a bank are.
    [InlineData("00fd9999", false)]
    // The same bank in both layers: the later layer's copy replaces the earlier one's.
    [InlineData("00448bd2", true)]
    public void A_copy_or_a_replacement_does_not_clash(string bank, bool edited)
    {
        if (Fixture.Read(SpkBankTests.PassBy) is not { } bytes) return;

        FolderModLayer first = Layer("first", "00448bd2", bytes);
        FolderModLayer second = Layer("second", bank, edited ? WithFirstRecordEdited(bytes) : bytes);

        Assert.Empty(SpkIdClashes.Find([first, second]));
    }

    private FolderModLayer Layer(string name, string bank, byte[] bytes)
    {
        string folder = Path.Combine(_sandbox, name, "mods", "soundbinary");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, bank + ".spk"), bytes);
        return new FolderModLayer(Path.Combine(_sandbox, name), name);
    }

    // The bank with byte 8 of its first record's core changed, a field any two encodings of a record differ in.
    private static byte[] WithFirstRecordEdited(byte[] bank)
    {
        byte[] edited = (byte[])bank.Clone();
        int preamble = 8 + 4 * BinaryPrimitives.ReadInt32LittleEndian(edited.AsSpan(4));
        int payload = preamble + 4 + 4 * BinaryPrimitives.ReadInt32LittleEndian(edited.AsSpan(preamble)) + 4;
        edited[payload + 8] ^= 0xFF;
        return edited;
    }
}
