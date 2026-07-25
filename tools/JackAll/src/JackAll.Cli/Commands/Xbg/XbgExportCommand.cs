using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using Spectre.Console.Cli;

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
            Vector3[] normals = sm.Normals ?? ComputeSmoothNormals(sm.Positions, sm.Indices);

            sb.AppendLine();
            sb.AppendLine($"g lod{sm.LodLevel}_part{sm.PartNumber}_submesh{submeshIndex}");
            sb.AppendLine($"usemtl {Sanitize(sm.MaterialName)}");

            foreach (Vector3 p in sm.Positions)
            {
                sb.AppendLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}");
            }
            foreach (Vector3 n in normals)
            {
                sb.AppendLine($"vn {F(n.X)} {F(n.Y)} {F(n.Z)}");
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

    /// <summary>Matches XbgFileHandler.ComputeSmoothNormals: accumulate each triangle's face normal into
    /// its three vertices and normalize, so a file with no NORMAL component still exports usable shading.</summary>
    private static Vector3[] ComputeSmoothNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            Vector3 faceNormal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normals[i]);
        }
        return normals;
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
