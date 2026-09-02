using System.Globalization;
using Spectre.Console;

namespace JackAll.Cli.Infrastructure;

/// <summary>Small shared file-in/file-out helpers so every command reads, resolves output paths, and
/// reports what it wrote the same way.</summary>
internal static class CliIO
{
    /// <summary>Reads a required input file, turning a missing path into a clean message
    /// <see cref="CliCommand{TSettings}"/>'s guard surfaces as one red line.</summary>
    public static byte[] ReadInput(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Input file not found: {path}");
        }
        return File.ReadAllBytes(path);
    }

    /// <summary>Text counterpart of <see cref="ReadInput"/>.</summary>
    public static string ReadInputText(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Input file not found: {path}");
        }
        return File.ReadAllText(path);
    }

    /// <summary>The explicit output path if one was given, otherwise <paramref name="defaultName"/> in
    /// the same directory as <paramref name="inputPath"/> — so `jackall xbt extract foo.xbt` drops
    /// foo.dds/foo.xml right next to foo.xbt without needing an -o.</summary>
    public static string ResolveOutput(string? explicitOut, string inputPath, string defaultName)
    {
        if (!string.IsNullOrWhiteSpace(explicitOut))
        {
            return explicitOut;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        return Path.Combine(dir, defaultName);
    }

    public static void WriteOutput(string path, byte[] bytes)
    {
        EnsureParentDirectory(path);
        File.WriteAllBytes(path, bytes);
    }

    public static void WriteOutput(string path, string text)
    {
        EnsureParentDirectory(path);
        File.WriteAllText(path, text);
    }

    public static void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>Refuses to write output back over the very file being read — the common footgun for the
    /// round-tripping encode/build commands, where a mistyped -o would silently clobber the source.</summary>
    public static void GuardNotOverwritingInput(string inputPath, string outputPath)
    {
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Output path is the same as the input ({inputPath}); refusing to overwrite it.");
        }
    }

    /// <summary>
    /// A CRC32 the user typed as hex, or null when what they typed is a name to be hashed instead.
    /// Accepts an optional <c>0x</c> and up to eight digits, so the same spelling works wherever a
    /// command takes "a hash or the thing it hashes".
    /// </summary>
    public static uint? TryParseHash(string text)
    {
        string digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return digits.Length is > 0 and <= 8 && digits.All(Uri.IsHexDigit)
            && uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash)
                ? hash
                : null;
    }

    public static void ReportWrote(string path) =>
        AnsiConsole.MarkupLine($"[green]Wrote[/] {path.EscapeMarkup()}");

    private static void EnsureParentDirectory(string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
