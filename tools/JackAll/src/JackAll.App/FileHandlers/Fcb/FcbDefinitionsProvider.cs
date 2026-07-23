using System.IO;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.FileHandlers.Fcb;

/// <summary>
/// The app-wide <see cref="FcbClassDefinitions"/> instance — binary_classes.xml is ~2400 lines and
/// every consumer (the `.fcb` handler, fragment preview/export) uses the same one, so it's loaded
/// once per session here rather than once per consumer.
/// </summary>
public static class FcbDefinitionsProvider
{
    public static readonly Lazy<FcbClassDefinitions> Value = new(() =>
        File.Exists(AppConfig.BinaryClassesFile)
            ? FcbClassDefinitions.Load(AppConfig.BinaryClassesFile)
            : FcbClassDefinitions.Empty);
}
