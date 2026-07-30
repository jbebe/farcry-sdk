using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core;

/// <summary>
/// Finds and loads the support assets that ship beside an executable, so every front end resolves
/// names and `.fcb` members the same way.
/// </summary>
/// <remarks>
/// Both CLIs copy <c>assets/fc2.hashlist</c> and <c>assets/binary_classes.xml</c> next to the exe as
/// <c>.itemhashes</c>/<c>.fcbclasses</c>; the walk-up fallback keeps a <c>dotnet run</c> straight from
/// source working too. Neither is fatal if missing - names fall back to bare hashes and `.fcb` members
/// to BinHex, exactly as in the App.
/// </remarks>
public static class BundledAssets
{
    public static NameDatabase LoadNames()
    {
        string? path = Find(".itemhashes") ?? Find(Path.Combine("assets", "fc2.hashlist"));
        return path is null ? NameDatabase.LoadFrom([]) : NameDatabase.Load(path);
    }

    public static FcbClassDefinitions LoadFcbClasses()
    {
        string? path = Find(".fcbclasses") ?? Find(Path.Combine("assets", "binary_classes.xml"));
        return path is null ? FcbClassDefinitions.Empty : FcbClassDefinitions.Load(path);
    }

    private static string? Find(string relativePath)
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}

/// <summary>
/// An <see cref="IProgress{T}"/> that reports on the calling thread.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Progress{T}"/>: with no synchronization context - which is exactly the
/// situation in a console app - that one marshals every report onto the thread pool, so lines arrive
/// out of order and often after the operation they described has finished. Here the reports are the
/// only sign of life during a multi-second archive mount, so they have to print as they happen.
/// </remarks>
public sealed class SyncProgress(Action<string> report) : IProgress<string>
{
    public void Report(string value) => report(value);
}
