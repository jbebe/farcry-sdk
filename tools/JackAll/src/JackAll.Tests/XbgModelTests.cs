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
    private const string Vehicle = "buggy.xbg";

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

    /// <summary>The prop's V bounds, read off its raw PMCU pair. They're asymmetric, so re-flipping
    /// V into bottom-up image space would move them to [-2.367, 1.664] and fail here.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Uvs_stay_in_the_games_top_down_texture_space()
    {
        if (!FixturesPresent) return;

        Vector2[] uvs = ParseFixture(Prop).Submeshes
            .Where(s => s.Uvs is not null).SelectMany(s => s.Uvs!).ToArray();

        Assert.Equal(-0.664, uvs.Min(uv => uv.Y), 3);
        Assert.Equal(3.367, uvs.Max(uv => uv.Y), 3);
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

    /// <summary>Each tier draws at least its target LOD's triangles - more when a part has nothing
    /// at that level and falls back to its own nearest.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Bake_picks_the_finest_lod_that_fits_the_budget()
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(Character);
        int finest = model.LodLevels.Max(l => TrianglesAt(model, l));
        int coarsest = model.LodLevels.Min(l => TrianglesAt(model, l));

        WorldModel unbounded = WorldModels.Bake(Character, model, int.MaxValue)!;
        Assert.InRange(unbounded.Fine.Count / 3, finest, finest + coarsest);
        Assert.InRange(unbounded.Coarse.Count / 3, coarsest, finest);

        // A budget nothing fits falls back to the coarsest LOD rather than to nothing.
        WorldModel squeezed = WorldModels.Bake(Character, model, 1)!;
        Assert.InRange(squeezed.Fine.Count / 3, coarsest, finest);
    }

    /// <summary>Two parts sharing one vertex buffer must not share its baked vertices: each bakes
    /// its own placement in, so the wheel and the body land in different places.</summary>
    [Fact]
    public void Bake_gives_parts_that_share_a_buffer_their_own_placement()
    {
        Vector3[] shared = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
        XbgSubmesh Part(string name, Matrix4x4? placement) => new()
        {
            LodLevel = 0,
            PartName = name,
            PartTransform = placement,
            MaterialIndex = name == "body" ? 0 : 1,
            MaterialName = name,
            Positions = shared,
            Indices = [0, 1, 2],
        };

        var model = new XbgModel
        {
            Materials = ["body", "wheel"],
            Submeshes = [Part("body", null), Part("wheel", Matrix4x4.CreateTranslation(0, 5, 0))],
            LodLevels = [0],
        };

        WorldModel baked = WorldModels.Bake("m.xbg", model, int.MaxValue)!;

        // Both parts emit their own three vertices, and only the wheel's are moved.
        Assert.Equal(6, baked.VertexCount);
        float[] y = [.. Enumerable.Range(0, 6).Select(i => baked.Vertices[i * WorldModel.FloatsPerVertex + 1])];
        Assert.Equal([0f, 0f, 1f], y[..3]);
        Assert.Equal([5f, 5f, 6f], y[3..]);
    }

    /// <summary>The buggy's four wheels are each modelled around their own pivot, so without the
    /// skeleton they render stacked inside the chassis. Placed, they sit at the four corners.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Rigid_parts_are_placed_by_the_bone_that_shares_their_name()
    {
        if (!File.Exists(Path.Combine(FixturesDir, Vehicle))) return;

        List<IGrouping<string, XbgSubmesh>> wheels = [.. ParseFixture(Vehicle).Submeshes
            .Where(s => s.LodLevel == 0 && s.PartName.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.PartName)];

        Dictionary<string, Vector3> centreByPart = wheels.ToDictionary(
            g => g.Key, Centre, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, centreByPart.Count);

        // Unplaced they all collapse onto the model origin.
        Assert.All(wheels, g => Assert.True(Centre(g.Select(Unplaced)).Length() < 0.2f));

        Vector3 backLeft = centreByPart["WheelBack_L_State01"];
        Vector3 backRight = centreByPart["WheelBack_R_State01"];
        Vector3 frontLeft = centreByPart["WheelFont_L_State01"];
        Vector3 frontRight = centreByPart["WheelFont_R_State01"];

        Assert.True(backLeft.X < 0 && frontLeft.X < 0, "left wheels sit on -X");
        Assert.True(backRight.X > 0 && frontRight.X > 0, "right wheels sit on +X");
        Assert.True(backLeft.Y < 0 && backRight.Y < 0, "rear wheels sit behind the origin");
        Assert.True(frontLeft.Y > 1.4f && frontRight.Y > 1.4f, "front wheels sit ahead of the origin");
    }

    private static Vector3 Centre(IEnumerable<XbgSubmesh> submeshes)
    {
        (Vector3 min, Vector3 max) = XbgModel.Bounds(submeshes);
        return (min + max) / 2f;
    }

    private static XbgSubmesh Unplaced(XbgSubmesh submesh) => new()
    {
        LodLevel = submesh.LodLevel,
        PartName = submesh.PartName,
        MaterialIndex = submesh.MaterialIndex,
        MaterialName = submesh.MaterialName,
        Positions = submesh.Positions,
        Indices = submesh.Indices,
    };

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
            name => MaterialSurface.None with { DiffuseTexturePath = $@"tex\{name}.xbt" })!;

        Assert.NotEmpty(baked.MaterialRanges);
        Assert.All(baked.MaterialRanges, r => Assert.Equal($@"tex\{r.MaterialName}.xbt", r.DiffuseTexturePath));
    }

    /// <summary>
    /// A file holds every state a part can be in and the engine shows one; the bake keeps the
    /// lowest-numbered, which is the intact one. The swamp boat is the clean case: its body and its
    /// three roof pieces each ship as STATE01 and STATE02, and drawing both puts the wrecked hull
    /// inside the whole one.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Only_the_intact_state_of_a_part_is_baked()
    {
        const string Boat = FixturesDir + "/swampboat.xbg";
        if (!File.Exists(Boat)) return;

        XbgModel model = XbgModel.Parse(File.ReadAllBytes(Boat));
        string[] all = [.. model.Submeshes.Select(s => s.PartName).Distinct()];
        Assert.Contains("BODY_STATE01", all);
        Assert.Contains("BODY_STATE02", all);

        WorldModel whole = WorldModels.Bake(Boat, model, WorldModels.FineTriangleBudget)!;
        WorldModel state01Only = WorldModels.Bake(Boat, model, WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string>(all.Where(p => !p.EndsWith("STATE02", StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase))!;

        // Baking the whole file already drops the STATE02 parts, so restricting to them changes
        // nothing - which it would not if both states were still being drawn.
        Assert.Equal(state01Only.Indices.Length, whole.Indices.Length);
    }

    /// <summary>
    /// A wardrobe file draws only the parts the entity wears. Left whole, every mercenary in the
    /// game renders all 111 pieces of merc_kit at once - seventeen heads on one body, and ten times
    /// the triangles of the outfit it should be wearing.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_named_part_list_bakes_only_those_parts()
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(Prop);
        string[] parts = [.. model.Submeshes.Where(s => s.LodLevel == 0)
            .Select(s => s.PartName).Where(p => p.Length > 0).Distinct()];
        if (parts.Length == 0) return;

        WorldModel whole = WorldModels.Bake(Prop, model, WorldModels.FineTriangleBudget)!;
        WorldModel one = WorldModels.Bake(Prop, model, WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string>([parts[0]], StringComparer.OrdinalIgnoreCase))!;

        Assert.True(one.Indices.Length <= whole.Indices.Length);
        Assert.True(one.Vertices.Length <= whole.Vertices.Length);

        // A list naming nothing the file has leaves no geometry at all, rather than falling back to
        // the whole mesh - the caller only passes a list the entity actually stated.
        Assert.Null(WorldModels.Bake(Prop, model, WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string>(["NO_SUCH_PART"], StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>Retail meshes carry two UV sets, and 99% of the corpus has the second one. It is
    /// what the "group" half of a material's tiling vector reads, so dropping it silently costs the
    /// mask and the second diffuse layer their coordinates.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Both_uv_sets_survive_the_parse()
    {
        if (!FixturesPresent) return;

        foreach (string name in new[] { Character, Prop })
        {
            foreach (XbgSubmesh submesh in ParseFixture(name).Submeshes.Where(s => s.LodLevel == 0))
            {
                Assert.NotNull(submesh.Uvs);
                Assert.NotNull(submesh.Uvs1);
                Assert.Equal(submesh.Uvs!.Length, submesh.Uvs1!.Length);
            }
        }
    }

    /// <summary>
    /// The bake hands the shader both channels the diffuse blend multiplies the mask by: green
    /// weights the two layers against each other, blue moves layer 1's tint from Base to Color1.
    /// Ten floats a vertex, and the last two are those, in that order.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Baked_vertices_carry_both_mask_channels()
    {
        if (!FixturesPresent) return;

        XbgModel model = ParseFixture(Prop);
        WorldModel baked = WorldModels.Bake(Prop, model, WorldModels.FineTriangleBudget)!;

        Assert.Equal(10, WorldModel.FloatsPerVertex);
        Assert.Equal(0, baked.Vertices.Length % WorldModel.FloatsPerVertex);

        XbgSubmesh source = model.Submeshes.First(s => s.LodLevel == 0 && s.Colours is not null);
        Vector4 first = source.Colours![source.Indices[0]];
        Assert.Equal(first.Y, baked.Vertices[8], 3);
        Assert.Equal(first.Z, baked.Vertices[9], 3);
    }

    /// <summary>
    /// Retail meshes are wound the way D3D wants: walk a triangle's indices in order and the
    /// cross product points away from the authored normal, into the surface. OpenGL reads that
    /// as back-facing on every outward triangle, which is why the model shader decides which side
    /// is showing by testing against the eye rather than trusting <c>gl_FrontFacing</c>. Reversing
    /// the index order here - or culling on GL's default winding - would erase the sun term.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Triangles_are_wound_clockwise_around_their_authored_normal()
    {
        if (!FixturesPresent) return;

        foreach (string name in new[] { Character, Prop })
        {
            int matchesWinding = 0, opposesWinding = 0;
            foreach (XbgSubmesh submesh in ParseFixture(name).Submeshes.Where(s => s.LodLevel == 0))
            {
                if (submesh.Normals is null) continue;

                for (int i = 0; i + 2 < submesh.Indices.Length; i += 3)
                {
                    int a = submesh.Indices[i], b = submesh.Indices[i + 1], c = submesh.Indices[i + 2];
                    Vector3 wound = Vector3.Cross(
                        submesh.Positions[b] - submesh.Positions[a],
                        submesh.Positions[c] - submesh.Positions[a]);
                    Vector3 authored = submesh.Normals[a] + submesh.Normals[b] + submesh.Normals[c];
                    if (wound.LengthSquared() < 1e-12f || authored.LengthSquared() < 1e-12f) continue;

                    if (Vector3.Dot(Vector3.Normalize(wound), Vector3.Normalize(authored)) > 0)
                    {
                        matchesWinding++;
                    }
                    else
                    {
                        opposesWinding++;
                    }
                }
            }

            // Not every triangle: a handful of degenerate slivers land either way.
            Assert.True(opposesWinding > (matchesWinding + opposesWinding) * 0.95,
                $"{name}: only {opposesWinding} of {matchesWinding + opposesWinding} triangles wind clockwise");
        }
    }

    private static int TotalVertices(XbgModel model)
        => model.Submeshes.Select(s => s.Positions).Distinct().Sum(p => p.Length);

    private static int TrianglesAt(XbgModel model, int lod)
        => model.Submeshes.Where(s => s.LodLevel == lod).Sum(s => s.Indices.Length) / 3;
}
