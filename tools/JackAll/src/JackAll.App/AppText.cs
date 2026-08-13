using System.Text;

namespace JackAll.App;

/// <summary>The app's one text codec for game content: UTF-8 with no BOM written, and a BOM
/// tolerated (stripped) when reading.</summary>
internal static class AppText
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static string DecodeUtf8(byte[] bytes) => Utf8.GetString(bytes).TrimStart('\uFEFF');

    public static byte[] EncodeUtf8(string text) => Utf8.GetBytes(text);
}
