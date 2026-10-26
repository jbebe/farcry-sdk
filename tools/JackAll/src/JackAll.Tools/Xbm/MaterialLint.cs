namespace JackAll.Tools.Xbm;

/// <summary>Something a material asks for that the game will not do; <see cref="Key"/> is null
/// when it is about the material as a whole.</summary>
public sealed record MaterialProblem(string? Key, string Message);

/// <summary>
/// Finds the mistakes the engine makes silently: a misspelt shader or key, a texture that is not
/// there, a value the condition overwrites.
/// </summary>
public static class MaterialLint
{
    /// <param name="exists">Whether a texture path resolves; texture paths go unchecked without it.</param>
    public static List<MaterialProblem> Check(XbmFile material, ShaderSchema schema, Func<string, bool>? exists = null)
    {
        List<MaterialProblem> problems = [];
        if (schema.Find(material.Shader) is not { } shader)
        {
            problems.Add(new MaterialProblem(null,
                $"No shader is named '{material.Shader}', so the game draws this with Error."
                + Nearest(material.Shader, schema.Shaders.Keys)));
            return problems;
        }

        foreach (XbmEntry entry in material.Entries.DistinctBy(e => e.Key))
        {
            if (shader.Find(entry.Key) is not { } parameter)
            {
                problems.Add(new MaterialProblem(entry.Key,
                    $"{shader.Name} takes no '{entry.Key}', so the game ignores it."
                    + Nearest(entry.Key, shader.Parameters.Select(p => p.Name))));
                continue;
            }

            if ((parameter.Section == XbmSection.Texture) != (entry.Section == XbmSection.Texture))
            {
                problems.Add(new MaterialProblem(entry.Key, parameter.Section == XbmSection.Texture
                    ? $"'{entry.Key}' takes a texture, not a number."
                    : $"'{entry.Key}' takes a number, not a texture."));
            }
            else if (entry.Section == XbmSection.Texture && exists is not null && !exists(entry.Path))
            {
                problems.Add(new MaterialProblem(entry.Key,
                    $"'{entry.Key}' names {entry.Path}, which is in neither the game nor the workspace."));
            }

            if (shader.CleanTwin(entry.Key) is { } clean && material.Find(clean) is { } twin && twin.Text != entry.Text)
            {
                problems.Add(new MaterialProblem(entry.Key,
                    $"'{entry.Key}' differs from '{clean}'. The condition overwrites it from '{clean}' and "
                    + $"'{entry.Key}Broken', so this value never shows; set '{clean}' instead."));
            }
        }
        return problems;
    }

    /// <summary>The engine matches names by case-sensitive hash, so a case slip is the likely one.</summary>
    private static string Nearest(string name, IEnumerable<string> names)
        => names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } near
            ? $" Names are case-sensitive: did you mean '{near}'?"
            : string.Empty;
}
