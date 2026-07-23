using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Tests;

/// <summary>
/// Checks the corpus against the same real shipped entitylibrary fragments <see cref="FcbValueCodecTests"/>
/// uses - the actual measurement of how much this recovers from a real savegame lives in the
/// <c>jackall savegame reverse</c> CLI command, not here; this just checks the harvesting mechanics
/// (declared-String-only, directory scan) against real data, plus the type-gating rule against a
/// hand-built synthetic tree.
/// </summary>
public class FcbStringCorpusTests
{
    private const string FixturesDir = "Fixtures/Fcb";
    private const string ClassesPath = "Fixtures/Fcb/binary_classes.xml";

    private static bool FixturesAvailable => Directory.Exists(FixturesDir) && File.Exists(ClassesPath);

    [Fact]
    public void Harvests_thousands_of_distinct_strings_from_real_entitylibrary_fragments()
    {
        if (!FixturesAvailable) return;

        FcbClassDefinitions defs = FcbClassDefinitions.Load(ClassesPath);
        FcbStringCorpus corpus = FcbStringCorpus.BuildFromDirectory(FixturesDir, defs);

        Assert.True(corpus.FilesLoaded >= 1);
        Assert.True(corpus.ByHash.Count > 1000,
            $"Only harvested {corpus.ByHash.Count} strings - fixtures may be empty/unreadable.");
    }

    [Fact]
    public void Every_harvested_hash_really_is_the_CRC32_of_its_string()
    {
        if (!FixturesAvailable) return;

        FcbClassDefinitions defs = FcbClassDefinitions.Load(ClassesPath);
        FcbStringCorpus corpus = FcbStringCorpus.BuildFromDirectory(FixturesDir, defs);

        foreach ((uint hash, string text) in corpus.ByHash)
        {
            Assert.Equal(hash, FcbClassDefinitions.Crc32Ascii(text));
        }
    }

    [Fact]
    public void Only_values_declared_String_by_the_resolved_class_are_harvested()
    {
        string classesXml =
            """
            <classes>
              <class name="Widget">
                <member name="Label">String</member>
                <member name="Count">Int32</member>
              </class>
            </classes>
            """;

        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        File.WriteAllText(tempPath, classesXml);
        try
        {
            FcbClassDefinitions defs = FcbClassDefinitions.Load(tempPath);

            var obj = new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii("Widget") };
            obj.Values[FcbClassDefinitions.Crc32Ascii("Label")] =
                [.. System.Text.Encoding.UTF8.GetBytes("hello"), 0];
            obj.Values[FcbClassDefinitions.Crc32Ascii("Count")] = BitConverter.GetBytes(42);

            var corpus = new FcbStringCorpus();
            corpus.AddTree(obj, defs);

            string harvested = Assert.Single(corpus.ByHash.Values);
            Assert.Equal("hello", harvested);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
