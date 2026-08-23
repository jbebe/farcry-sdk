using JackAll.Tools.Fc2Model;
using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// Building a pack for a shipped model and applying it back.
/// </summary>
/// <remarks>
/// The pack is the point of the whole exercise: one model, decoded, with no Dunia format inside it,
/// so an editor can open it carrying no format code. What is held here is that the trip is
/// lossless where it can be - mesh, material and rig come back byte for byte - and that an untouched
/// pack writes nothing at all, which is what stops a texture decaying a little on every save.
/// </remarks>
public sealed class Fc2ModelBundleTests
{
    private const string Rifle = "graphics/weapons/primary/ak47/ak47.xbg";

    [Fact]
    public void A_pack_carries_the_model_and_everything_it_names()
    {
        if (Reader() is not { } read)
        {
            return;
        }

        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(Rifle, read);

        Assert.Equal(Rifle, bundle.Manifest.Model);
        Assert.Equal(Fc2ModelBundle.CurrentVersion, bundle.Manifest.Version);
        Assert.NotNull(bundle.Entry(Rifle));
        Assert.Contains(bundle.Manifest.Entries, entry => entry.Kind == Fc2ModelKind.Rig);
        Assert.Contains(bundle.Manifest.Entries, entry => entry.Kind == Fc2ModelKind.Material);
        Assert.Contains(bundle.Manifest.Entries, entry => entry.Kind == Fc2ModelKind.Texture);

        // Every entry's content is present and hashes to what the manifest says.
        foreach (Fc2ModelEntry entry in bundle.Manifest.Entries)
        {
            Assert.Equal(entry.Sha256, Fc2ModelBundle.Hash(bundle.Content(entry)));
            Assert.False(entry.Modified, $"{entry.Path} is marked modified in a freshly built pack.");
        }

        // The rifle's own textures sit beside it and are its to edit; the shared detail maps its
        // materials tile over are not.
        Fc2ModelEntry mesh = bundle.Entry(Rifle)!;
        Assert.Equal(Fc2ModelBundle.Owned, mesh.Role);
        Assert.Contains(bundle.Manifest.Entries, entry => entry.Role == Fc2ModelBundle.Shared);
    }

    [Fact]
    public void A_pack_survives_the_zip()
    {
        if (Reader() is not { } read)
        {
            return;
        }

        Fc2ModelBundle built = Fc2ModelBuilder.Build(Rifle, read);
        string path = Path.Combine(Path.GetTempPath(), $"fc2model-{Guid.NewGuid():N}{Fc2ModelBundle.Extension}");
        try
        {
            built.Save(path);
            Fc2ModelBundle loaded = Fc2ModelBundle.Load(path);

            Assert.Equal(built.Manifest.Model, loaded.Manifest.Model);
            Assert.Equal(built.Manifest.Entries.Count, loaded.Manifest.Entries.Count);
            foreach (Fc2ModelEntry entry in loaded.Manifest.Entries)
            {
                Assert.Equal(entry.Sha256, Fc2ModelBundle.Hash(loaded.Content(entry)));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// An untouched pack asks for nothing to be written, which is what keeps a texture from being
    /// compressed again on every round trip.
    /// </summary>
    [Fact]
    public void An_untouched_pack_writes_nothing()
    {
        if (Reader() is not { } read)
        {
            return;
        }

        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(Rifle, read);
        Assert.Empty(Fc2ModelApplier.Outputs(bundle));
    }

    /// <summary>
    /// The formats that decode losslessly have to come back byte for byte, or the pack is not a
    /// safe place to keep a model.
    /// </summary>
    [Fact]
    public void Mesh_material_and_rig_come_back_byte_for_byte()
    {
        if (Reader() is not { } read)
        {
            return;
        }

        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(Rifle, read);
        List<Fc2ModelOutput> outputs = Fc2ModelApplier.Outputs(bundle, onlyModified: false);

        int compared = 0;
        foreach (Fc2ModelOutput output in outputs)
        {
            if (bundle.Entry(output.Path) is not { } entry || entry.Kind == Fc2ModelKind.Texture)
            {
                continue;
            }

            compared++;
            Assert.True(
                output.Content.AsSpan().SequenceEqual(read(output.Path)),
                Fc2Corpus.DescribeDifference(output.Path, read(output.Path)!, output.Content));
        }

        Assert.True(compared >= 3, $"Only {compared} lossless entries were compared.");
    }

    /// <summary>
    /// Editing something the model shares with others is refused rather than quietly re-skinning
    /// them - one shared detail map backs 46 of the 87 shipped weapons.
    /// </summary>
    [Fact]
    public void Editing_a_shared_file_is_refused()
    {
        if (Reader() is not { } read)
        {
            return;
        }

        Fc2ModelBundle bundle = Fc2ModelBuilder.Build(Rifle, read);
        Fc2ModelEntry shared = bundle.Manifest.Entries.First(entry => entry.Role == Fc2ModelBundle.Shared);
        shared.OriginSha256 = shared.Sha256;

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => Fc2ModelApplier.Outputs(bundle));
        Assert.Contains(shared.Path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Reader() is not null, Fc2Corpus.MissingMessage(".xbg"));

    /// <summary>Reads a game path out of the corpus, matching on the tail of the path.</summary>
    private static Func<string, byte[]?>? Reader()
    {
        if (!Fc2Corpus.Present)
        {
            return null;
        }

        Dictionary<string, string> byTail = new(StringComparer.OrdinalIgnoreCase);
        foreach (string extension in (string[])[".xbg", ".xbm", ".xbt", ".skeleton"])
        {
            foreach (string path in Fc2Corpus.Find(extension))
            {
                byTail[Path.GetFileName(path)] = path;
            }
        }

        return wanted =>
        {
            string name = Path.GetFileName(wanted.Replace('\\', '/'));
            return byTail.TryGetValue(name, out string? found) ? File.ReadAllBytes(found) : null;
        };
    }
}
