using JackAll.Core;

namespace JackAll.App;

/// <summary>
/// The app-wide <see cref="VanillaHashes"/> instance, shared between <see cref="MainViewModel"/>
/// (checked at startup, see <see cref="MainViewModel.InitializeAsync"/>) and
/// <see cref="MainWindow"/> (checked again after <c>RestoreVanilla_Click</c>) — both need the same
/// reference hashes, and finding mismatches is only meaningful once per install, not once per check.
/// </summary>
public static class VanillaHashesProvider
{
    public static readonly Lazy<VanillaHashes> Value = new(() => VanillaHashes.Load(AppConfig.VanillaHashesFile));
}
