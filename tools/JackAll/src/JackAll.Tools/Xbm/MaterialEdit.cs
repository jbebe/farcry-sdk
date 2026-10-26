namespace JackAll.Tools.Xbm;

/// <summary>Sets a material's properties from text, the one edit rule the editor and the CLI share.</summary>
public static class MaterialEdit
{
    /// <summary>
    /// Set a key, stored as the material already stores it or else as <paramref name="shader"/>
    /// declares it; <paramref name="link"/> also sets a condition-driven value's clean twin.
    /// </summary>
    /// <returns>The keys written.</returns>
    public static List<string> Set(XbmFile material, ShaderDescriptor? shader, string key, string text, bool link)
    {
        List<string> written = [];
        foreach (string target in link && shader?.CleanTwin(key) is { } clean ? [key, clean] : (string[])[key])
        {
            (XbmSection section, int width) = material.Find(target) is { } current
                ? (current.Section, current.Floats.Length)
                : shader?.Find(target) is { } declared
                    ? (declared.Section, declared.Width)
                    : throw new KeyNotFoundException($"{shader?.Name ?? material.Shader} takes no '{target}'.");
            material.Set(XbmEntry.Parse(target, section, width, text));
            written.Add(target);
        }
        return written;
    }
}
