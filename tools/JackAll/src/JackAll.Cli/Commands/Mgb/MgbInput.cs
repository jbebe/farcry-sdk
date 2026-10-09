using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Mgb;

namespace JackAll.Cli.Commands.Mgb;

/// <summary>A package given as a binary <c>.mgb</c> or as the XML it is built from.</summary>
internal static class MgbInput
{
    /// <summary>The package's bytes, with the names an XML source declares caught in
    /// <paramref name="names"/> - the binary keeps only their hashes.</summary>
    /// <remarks>The magic decides rather than the extension, and XML is re-read as text so its
    /// encoding declaration counts.</remarks>
    public static byte[] Read(string path, MgbNameLookup names)
    {
        byte[] input = CliIO.ReadInput(path);
        return MgbPackage.HasMagic(input) ? input : MgbXml.Encode(CliIO.ReadInputText(path), names);
    }
}
