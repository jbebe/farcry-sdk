using System.Numerics;
using JackAll.Tools.Xbg;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Pins <see cref="XbgModel"/> against two retail meshes (a skinned character and a static prop)
/// now that the map renders through it. The exact counts are regression pins recorded from a
/// known-good parse; a change in any of them means the chunk walk drifted, not that the fixtures
/// changed.
/// </summary>
public class XbgModelTests
{
    private const string FixturesDir = "Fixtures/Xbg";
    private const string Character = "andrehyppolite.xbg";
    private const string Prop = "chairbar01.xbg";

    private static bool FixturesPresent
        => File.Exists(Path.Combine(FixturesDir, Character)) && File.Exists(Path.Combine(FixturesDir, Prop));

    private static XbgModel ParseFixture(string name)
        => XbgModel.Parse(File.ReadAllBytes(Path.Combine(FixturesDir, name)));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            FixturesPresent,
            $"{FixturesDir} is missing {Character} / {Prop} (linked from tmp\\graphics when the "
            + "game export exists), so every fixture-backed test in this class silently no-opped.");

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_character_parses_to_the_recorded_shape()
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(Character);
        Assert.Equal(
            (46, 4, 14, 17175),
            (model.Submeshes.Count, model.LodLevels.Count, model.Materials.Count, TotalVertices(model)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_prop_parses_to_the_recorded_shape()
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(Prop);
        Assert.Equal(
            (5, 3, 2, 912),
            (model.Submeshes.Count, model.LodLevels.Count, model.Materials.Count, TotalVertices(model)));
    }

    /// <summary>Retail material entries are archive paths, which is what the map's texture
    /// resolution reads them as.</summary>
    [Theory]
    [InlineData(Character)]
    [InlineData(Prop)]
    [Trait("Category", "RequiresFixture")]
    public void Material_entries_are_xbm_archive_paths(string fixture)
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(fixture);
        Assert.NotEmpty(model.Materials);
        Assert.All(model.Materials, m => Assert.EndsWith(".xbm", m, StringComparison.OrdinalIgnoreCase));
        Assert.All(model.Materials, m => Assert.StartsWith(@"GRAPHICS\", m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A person is under 2.5 m in any direction; a blown-up extent means the PMCP
    /// position scale stopped being applied.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_character_has_a_person_sized_nondegenerate_extent()
    {
        if (!FixturesPresent) return;

        (Vector3 min, Vector3 max) = XbgModel.Bounds(ParseFixture(Character).Submeshes);
        Assert.True(min.X < max.X && min.Y < max.Y && min.Z < max.Z, $"degenerate bounds {min}..{max}");
        Assert.True((max - min).Length() < 5f, $"implausible extent {(max - min).Length()}m");
    }

    [Theory]
    [InlineData(Character)]
    [InlineData(Prop)]
    [Trait("Category", "RequiresFixture")]
    public void Every_submesh_is_structurally_sound(string fixture)
    {
        if (!FixturesPresent) return;

        foreach (XbgSubmesh submesh in ParseFixture(fixture).Submeshes)
        {
            Assert.Equal(0, submesh.Indices.Length % 3);
            Assert.All(submesh.Indices, i => Assert.InRange(i, 0, submesh.Positions.Length - 1));
            if (submesh.Normals is { } normals)
            {
                Assert.Equal(submesh.Positions.Length, normals.Length);
            }
        }
    }

    [Theory]
    [InlineData(Character)]
    [InlineData(Prop)]
    [Trait("Category", "RequiresFixture")]
    public void Uvs_match_their_positions_and_stay_finite(string fixture)
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(fixture);
        Assert.Contains(model.Submeshes, s => s.Uvs is not null);
        foreach (XbgSubmesh submesh in model.Submeshes)
        {
            if (submesh.Uvs is not { } uvs)
            {
                continue;
            }

            Assert.Equal(submesh.Positions.Length, uvs.Length);
            Assert.All(uvs, uv => Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y)));
        }
    }

    [Fact]
    public void Smooth_normals_are_unit_length_with_a_fallback_for_unreferenced_vertices()
    {
        Vector3[] positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(9, 9, 9)];
        Vector3[] normals = XbgModel.ComputeSmoothNormals(positions, [0, 1, 2]);

        Assert.All(normals, n => Assert.Equal(1f, n.Length(), 3));
        Assert.Equal(Vector3.UnitZ, normals[0]);
        Assert.Equal(Vector3.UnitY, normals[3]);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Bake_picks_the_finest_lod_that_fits_the_budget()
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(Character);
        int finest = model.LodLevels.Max(l => TrianglesAt(model, l));
        int coarsest = model.LodLevels.Min(l => TrianglesAt(model, l));

        WorldModel unbounded = WorldModels.Bake(Character, model, int.MaxValue)!;
        Assert.Equal(finest, unbounded.Fine.Count / 3);
        Assert.Equal(coarsest, unbounded.Coarse.Count / 3);

        // A budget nothing fits falls back to the coarsest LOD rather than to nothing.
        WorldModel squeezed = WorldModels.Bake(Character, model, 1)!;
        Assert.Equal(coarsest, squeezed.Fine.Count / 3);
    }

    [Theory]
    [InlineData(Character)]
    [InlineData(Prop)]
    [Trait("Category", "RequiresFixture")]
    public void Baked_arrays_are_internally_consistent(string fixture)
    {
        if (!FixturesPresent) return;

        WorldModel baked = WorldModels.Bake(fixture, ParseFixture(fixture), WorldModels.FineTriangleBudget)!;

        Assert.Equal(0, baked.Vertices.Length % WorldModel.FloatsPerVertex);
        Assert.Equal(0, baked.Indices.Length % 3);
        Assert.All(baked.Indices, i => Assert.InRange(i, 0, baked.VertexCount - 1));

        // The two tiers and the material ranges must tile actual index data.
        Assert.Equal(baked.Indices.Length, baked.Fine.Count + (baked.Coarse == baked.Fine ? 0 : baked.Coarse.Count));
        Assert.Equal(baked.Fine.Count, baked.MaterialRanges.Sum(r => r.Count));
        Assert.All(baked.MaterialRanges, r => Assert.InRange(r.Start, baked.Fine.Start, baked.Fine.Start + baked.Fine.Count - r.Count));
    }

    /// <summary>The material resolver's answer must land on the range that named the material.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Baked_material_ranges_carry_their_resolved_diffuse_texture()
    {
        if (!FixturesPresent) return;

        WorldModel baked = WorldModels.Bake(
            Character, ParseFixture(Character), WorldModels.FineTriangleBudget,
            name => $@"tex\{name}.xbt")!;

        Assert.NotEmpty(baked.MaterialRanges);
        Assert.All(baked.MaterialRanges, r => Assert.Equal($@"tex\{r.MaterialName}.xbt", r.DiffuseTexturePath));
    }

    private static int TotalVertices(XbgModel model)
        => model.Submeshes.Select(s => s.Positions).Distinct().Sum(p => p.Length);

    private static int TrianglesAt(XbgModel model, int lod)
        => model.Submeshes.Where(s => s.LodLevel == lod).Sum(s => s.Indices.Length) / 3;
}
