using JackAll.Tools.World;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// Full-chain validation against the real game's exported graphics tree (tmp/graphics, absent on
/// CI): mesh -> material archive path -> .xbm -> albedo .xbt -> uploadable DXT surface. This is
/// the chain the map's model layer runs; the per-format details are pinned by the narrower
/// fixture tests, this proves they compose on retail data.
/// </summary>
public class GraphicsExportTests
{
    private static readonly string? Root = FindRoot();

    private static string? FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tmp", "graphics");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Maps an archive path like graphics\actors\...\x.xbt onto the export folder.</summary>
    private static byte[]? ReadByPath(string gamePath)
    {
        string relative = gamePath.StartsWith(@"graphics\", StringComparison.OrdinalIgnoreCase)
            ? gamePath[9..]
            : gamePath;
        string full = Path.Combine(Root!, relative);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    [Theory]
    [InlineData(@"actors\buddy_andrehyppolite\andrehyppolite.xbg")]
    [InlineData(@"objects\furnitures\chairs\chairbar01.xbg")]
    [Trait("Category", "RequiresFixture")]
    public void A_retail_mesh_resolves_every_material_to_a_decodable_texture(string mesh)
    {
        if (Root is null) return;

        XbgModel model = XbgModel.Parse(File.ReadAllBytes(Path.Combine(Root, mesh)));
        WorldModel baked = WorldModels.Bake(
            mesh, model, WorldModels.FineTriangleBudget, WorldModels.DiffuseResolver(ReadByPath))!;

        Assert.NotEmpty(baked.MaterialRanges);
        foreach (MaterialRange range in baked.MaterialRanges)
        {
            Assert.False(string.IsNullOrEmpty(range.DiffuseTexturePath),
                $"material {range.MaterialName} resolved no albedo texture");

            byte[]? xbt = ReadByPath(range.DiffuseTexturePath!);
            Assert.False(xbt is null, $"albedo {range.DiffuseTexturePath} not found in the export");

            (_, byte[] dds) = XbtTexture.Split(xbt!);
            Assert.False(DdsSurface.TryParse(dds) is null,
                $"albedo {range.DiffuseTexturePath} is not a plain DXT surface");
        }
    }
}
