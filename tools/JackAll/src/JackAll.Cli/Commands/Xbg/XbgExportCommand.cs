using JackAll.Cli.Infrastructure;
using JackAll.Tools.Xbg;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JackAll.Cli.Commands.Xbg;

/// <summary>
/// Exports an .xbg's geometry to a Wavefront <c>.obj</c> — vertex positions, normals (real ones when
/// the file carries a NORMAL component, otherwise smooth normals accumulated from face geometry the
/// same way the App's viewer does), and triangle lists grouped per submesh with a <c>usemtl</c> named
/// after each submesh's material. This is the geometry-only slice <see cref="XbgModel"/> decodes (no
/// UVs/skinning/textures — see its remarks), so the .obj is a mesh preview, not a full re-import asset.
/// </summary>
public sealed class XbgExportCommand : CliCommand<XbgExportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xbg>")]
        [Description("The .xbg mesh to convert.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.obj>")]
        [Description("Output .obj path (default: the input path with an .obj extension).")]
        public string? Out { get; init; }

        [CommandOption("--lod <n>")]
        [Description("Which LOD to export (default: the most detailed one). Ignored with --all-lods.")]
        public int? Lod { get; init; }

        [CommandOption("--all-lods")]
        [Description("Export every LOD into the one .obj instead of just one.")]
        public bool AllLods { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] data = CliIO.ReadInput(settings.Input);
        XbgModel model = XbgModel.Parse(data);

        if (model.Submeshes.Count == 0)
        {
            throw new InvalidDataException(
                "No renderable geometry decoded (the DNKS submesh table didn't match this file's layout, or the mesh is empty).");
        }

        List<XbgSubmesh> selected;
        if (settings.AllLods)
        {
            selected = [.. model.Submeshes];
        }
        else
        {
            int lod = settings.Lod ?? model.LodLevels[0];
            if (!model.LodLevels.Contains(lod))
            {
                throw new InvalidDataException($"LOD {lod} isn't in this file (available: {string.Join(", ", model.LodLevels)}).");
            }
            selected = model.Submeshes.Where(s => s.LodLevel == lod).ToList();
        }

        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".obj");
        CliIO.WriteOutput(outPath, BuildObj(selected, Path.GetFileName(settings.Input)));

        CliIO.ReportWrote(outPath);
        return 0;
    }

    private static string BuildObj(List<XbgSubmesh> submeshes, string sourceName)
    {
        var sb = new StringBuilder();
        sb.Append("# Wavefront OBJ exported from ").AppendLine(sourceName);
        sb.AppendLine("# via jackall xbg export (positions + normals only; no UVs/skinning/textures)");

        int vertexBase = 1; // .obj indices are 1-based and global across the whole file
        int submeshIndex = 0;
        foreach (XbgSubmesh sm in submeshes)
        {
            Vector3[] normals = sm.Normals ?? XbgModel.ComputeSmoothNormals(sm.Positions, sm.Indices);

            sb.AppendLine();
            sb.AppendLine($"g lod{sm.LodLevel}_{Sanitize(sm.PartName)}_submesh{submeshIndex}");
            sb.AppendLine($"usemtl {Sanitize(sm.MaterialName)}");

            foreach (Vector3 p in sm.Positions)
            {
                Vector3 placed = sm.Place(p);
                sb.AppendLine($"v {F(placed.X)} {F(placed.Y)} {F(placed.Z)}");
            }
            foreach (Vector3 n in normals)
            {
                Vector3 placed = sm.PlaceNormal(n);
                sb.AppendLine($"vn {F(placed.X)} {F(placed.Y)} {F(placed.Z)}");
            }

            for (int i = 0; i + 2 < sm.Indices.Length; i += 3)
            {
                int a = vertexBase + sm.Indices[i];
                int b = vertexBase + sm.Indices[i + 1];
                int c = vertexBase + sm.Indices[i + 2];
                // Positions and normals are parallel per-vertex arrays, so a vertex's normal index
                // equals its position index.
                sb.AppendLine($"f {a}//{a} {b}//{b} {c}//{c}");
            }

            vertexBase += sm.Positions.Length;
            submeshIndex++;
        }

        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Sanitize(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "material";
        }
        var sb = new StringBuilder(trimmed.Length);
        foreach (char c in trimmed)
        {
            sb.Append(char.IsWhiteSpace(c) ? '_' : c);
        }
        return sb.ToString();
    }
}
