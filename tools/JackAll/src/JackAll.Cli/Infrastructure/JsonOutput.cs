using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Cli.Infrastructure;

/// <summary>
/// Implemented by any command's settings that offer <c>--json</c>, so
/// <see cref="CliCommand{TSettings}"/>'s error guard can report failures in the same shape as
/// successes instead of printing a red line a machine can't read.
/// </summary>
public interface IJsonOutputSettings
{
    bool Json { get; }
}

/// <summary>
/// The machine-readable half of the CLI: exactly one JSON object on stdout, nothing else.
/// </summary>
/// <remarks>
/// The contract deliberately has an <c>ok</c> discriminator rather than relying on the exit code
/// alone. A caller (JackAll's Vortex extension, primarily) has to be able to tell "the game folder
/// isn't a Far Cry 2 install" apart from "the process died", and an exit code can't carry the
/// message that makes the difference actionable.
///
/// Progress and diagnostics go to <b>stderr</b> (see <see cref="Report"/>) precisely so stdout stays
/// a single parseable document — a caller can pipe stderr into a progress bar and still
/// <c>JSON.parse</c> stdout whole.
/// </remarks>
internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write<T>(T payload) => Console.Out.WriteLine(JsonSerializer.Serialize(payload, Options));

    public static void WriteError(string message) => Write(new { ok = false, error = message });

    /// <summary>Human-facing progress, always on stderr so it never contaminates the JSON document
    /// and stays visible in a normal terminal run too.</summary>
    public static void Report(string message) => Console.Error.WriteLine(message);
}
