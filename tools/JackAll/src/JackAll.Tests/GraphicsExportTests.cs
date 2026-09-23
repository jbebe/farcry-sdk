using JackAll.Tools.World;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// The whole chain the map's model layer runs, on a retail prop: mesh -> material archive path ->
/// .xbm -> albedo .xbt -> uploadable DXT surface. The per-format details are pinned by the narrower
/// fixture tests; this proves they compose.
/// </summary>
public class GraphicsExportTests
{
    // The prop's materials and albedos, looked up by file name.
    private static readonly Func<string, byte[]?> ReadByPath = Fixture.ByFileName("GraphicsExport");

    [Fact]
    public void A_retail_mesh_resolves_every_material_to_a_decodable_texture()
    {
        if (Fixture.Read(XbgFixtures.Prop) is not { } bytes)
        {
            return;
        }

        WorldModel baked = WorldModels.Bake(
            XbgFixtures.Prop, XbgModel.Parse(bytes), WorldModels.FineTriangleBudget, WorldModels.SurfaceResolver(ReadByPath))!;

        Assert.NotEmpty(baked.MaterialRanges);
        foreach (MaterialRange range in baked.MaterialRanges)
        {
            Assert.False(string.IsNullOrEmpty(range.DiffuseTexturePath),
                $"material {range.MaterialName} resolved no albedo texture");

            byte[]? xbt = ReadByPath(range.DiffuseTexturePath!);
            Assert.False(xbt is null, $"albedo {range.DiffuseTexturePath} is not among the fixtures");

            (_, byte[] dds) = XbtTexture.Split(xbt!);
            Assert.False(DdsSurface.TryParse(dds) is null,
                $"albedo {range.DiffuseTexturePath} is not a plain DXT surface");
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            "GraphicsExport/trodrigue-060927-56723838.xbm", "GraphicsExport/trodrigue-061024-47283710.xbm",
            "GraphicsExport/metalburned_01_d.xbt", "GraphicsExport/fabrickevlar_01_d.xbt");
}
