namespace JackAll.Tools.Reach;

/// <summary>
/// Turns the verdict list's machine reasons into a sentence someone reading a file browser can act
/// on. The raw reason stays the record of record - this only decides how it reads.
/// </summary>
public static class ReachReasons
{
    public static string Explain(string reason) => reason switch
    {
        "console-only" =>
            "it is a PlayStation 3 or Xbox 360 leftover, and the PC build never selects it.",
        "fallback:primary-present" =>
            "it is the readable twin of a binary file beside it, which the engine loads instead.",
        "fallback:flag-selected" =>
            "the engine picks the other variant of this file; the flag that would select this one is never set.",
        "unreachable:rml-source" =>
            "it is the XML the .rml beside it was compiled from, and the engine only ever loads the .rml.",
        "unreachable:authoring-twin" =>
            "it is an authoring copy kept beside the file the engine actually loads.",
        "unreachable:qa-scaffolding" =>
            "it is QA test scaffolding rather than shipped content.",
        "unreachable:trade-show-demo" =>
            "it is a trade-show demo script rather than shipped content.",
        _ when reason.StartsWith("dev-leftover", StringComparison.Ordinal) =>
            "it belongs to a development world slot that shipped un-stripped.",
        _ =>
            "nothing the engine can name points at it.",
    };
}
