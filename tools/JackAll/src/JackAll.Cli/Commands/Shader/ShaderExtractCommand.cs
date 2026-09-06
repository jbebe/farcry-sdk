using JackAll.Cli.Infrastructure;
using JackAll.Tools.Shader;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace JackAll.Cli.Commands.Shader;

/// <summary>
/// Splits a <c>.pso</c>/<c>.vso</c> into its bare Direct3D bytecode and a companion <c>.xml</c>
/// binding table — the same pair <c>shader build</c> reassembles. The bytecode is what
/// <c>fxc /dumpbin</c> disassembles.
/// </summary>
public sealed class ShaderExtractCommand : CliCommand<ShaderExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.pso|file.vso>")]
        [Description("The shader object to split.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out-dir <dir>")]
        [Description("Directory for the .bin/.xml pair (default: next to the input).")]
        public string? OutDir { get; init; }

        [CommandOption("-n|--names <file>")]
        [Description("Newline-separated parameter names, used to resolve the table's CRC32s.")]
        public string? Names { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        ShaderObject shader = ShaderObject.Parse(CliIO.ReadInput(settings.Input));

        string baseName = Path.GetFileNameWithoutExtension(settings.Input);
        string binPath = CliIO.ResolveOutput(
            settings.OutDir is null ? null : Path.Combine(settings.OutDir, baseName + ".bin"),
            settings.Input, baseName + ".bin");
        string xmlPath = Path.ChangeExtension(binPath, ".xml");

        CliIO.WriteOutput(binPath, shader.Bytecode);
        CliIO.WriteOutput(xmlPath, shader.ToXml(LoadNames(settings.Names)));

        CliIO.ReportWrote(binPath);
        CliIO.ReportWrote(xmlPath);
        return 0;
    }

    /// <summary>
    /// A CRC32-keyed dictionary of the candidate names in <paramref name="path"/>. The engine hashes
    /// a parameter's name in its exact case, unlike the lowercased archive paths <c>NameHash</c> deals
    /// in, so the file's own casing is what counts.
    /// </summary>
    internal static Dictionary<uint, string>? LoadNames(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var names = new Dictionary<uint, string>();
        foreach (string line in File.ReadLines(path))
        {
            string name = line.Trim();
            if (name.Length != 0)
            {
                names[Crc32.HashToUInt32(Encoding.ASCII.GetBytes(name))] = name;
            }
        }
        return names;
    }
}
