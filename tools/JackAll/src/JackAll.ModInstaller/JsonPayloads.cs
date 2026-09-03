using System.Text.Json.Serialization;

namespace JackAll.ModInstaller;

/// <summary>
/// The <c>--json</c> documents, one record per command, matching <c>jackall-cli mod</c>'s output
/// field for field so a caller can point at either exe.
/// </summary>
/// <remarks>
/// Concrete records with a source-generated serializer, not anonymous types through the reflection
/// serializer: reflection-based <c>JsonSerializer</c> is exactly what <c>PublishTrimmed</c> cannot see
/// through, and it's the only thing in this project that would have needed it. Nullable members are
/// omitted when null (see <see cref="ModInstallerJson"/>), which is how one status record covers both
/// the valid and the "not a Far Cry 2 install" answer.
/// </remarks>
internal sealed record StatusPayload
{
    public bool Ok { get; init; } = true;
    public string GamePath { get; init; } = string.Empty;
    public bool Valid { get; init; }
    public string? Error { get; init; }
    public string? DataDir { get; init; }
    public string? PatchFat { get; init; }
    public string? PatchDat { get; init; }
    public bool? HasVanillaBackup { get; init; }
    public bool? LooksModded { get; init; }
    public int? PatchEntries { get; init; }
    /// <summary>The one state a caller must refuse to build from without an explicit override.</summary>
    public bool? NeedsVanillaConfirmation { get; init; }
}

internal sealed record BuildPayload
{
    public bool Ok { get; init; } = true;
    public string PatchFat { get; init; } = string.Empty;
    public string PatchDat { get; init; } = string.Empty;
    public int TotalEntries { get; init; }
    public int VanillaEntries { get; init; }
    public int OverriddenEntries { get; init; }
    public int AddedEntries { get; init; }
    public long OutputBytes { get; init; }
    public int PluginsDeployed { get; init; }
    public int PluginsRemoved { get; init; }
    /// <summary>Plugin paths whose target in bin\plugins exists but isn't JackAll's — left untouched.</summary>
    public IReadOnlyList<string> PluginCollisions { get; init; } = [];
    public IReadOnlyList<BuildLayerPayload> Layers { get; init; } = [];
    public IReadOnlyList<ConflictPayload> Conflicts { get; init; } = [];
}

internal sealed record BuildLayerPayload
{
    public int Index { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int WholeFileOverrides { get; init; }
    public int FragmentOverrides { get; init; }
    public int PluginFiles { get; init; }
}

/// <summary>A fragment two layers both edited, resolved by load order rather than refusing to build.</summary>
internal sealed record ConflictPayload
{
    public string Container { get; init; } = string.Empty;
    public string FragmentId { get; init; } = string.Empty;
    public bool IsNewEntry { get; init; }
    public string WinningLayer { get; init; } = string.Empty;
    public IReadOnlyList<string> EarlierLayers { get; init; } = [];
}

internal sealed record ImportLegacyPayload
{
    public bool Ok { get; init; } = true;
    public string OutDir { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int TotalEntries { get; init; }
    public int Imported { get; init; }
    public int FragmentsImported { get; init; }
    public int Skipped { get; init; }
    public int StagedFiles { get; init; }

    /// <summary>Containers left out, as <c>&lt;path&gt;: &lt;reason&gt;</c> - see
    /// <see cref="LegacyImportNote"/>.</summary>
    public IReadOnlyList<string> Refused { get; init; } = [];

    /// <summary>Containers staged whole rather than per fragment, in the same shape as
    /// <see cref="Refused"/>. Imported, but without their per-fragment merging.</summary>
    public IReadOnlyList<string> WholeFile { get; init; } = [];
}

internal sealed record RestorePayload
{
    public bool Ok { get; init; } = true;
    public bool Restored { get; init; } = true;
    public string PatchFat { get; init; } = string.Empty;
    public string PatchDat { get; init; } = string.Empty;
    public int PluginsRemoved { get; init; }
}

/// <summary>
/// Failure in the same shape as success. The <c>ok</c> discriminator rather than the exit code alone,
/// because a caller has to tell "that folder isn't Far Cry 2" apart from "the process died", and an
/// exit code can't carry the message that makes the difference actionable.
/// </summary>
internal sealed record ErrorPayload
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StatusPayload))]
[JsonSerializable(typeof(BuildPayload))]
[JsonSerializable(typeof(ImportLegacyPayload))]
[JsonSerializable(typeof(RestorePayload))]
[JsonSerializable(typeof(ErrorPayload))]
internal partial class ModInstallerJson : JsonSerializerContext;
