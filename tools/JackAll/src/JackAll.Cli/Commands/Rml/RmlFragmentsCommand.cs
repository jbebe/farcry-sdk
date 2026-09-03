using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Rml;

/// <summary>
/// Writes the strings a table changes against vanilla as one localization patch document.
/// </summary>
/// <remarks>
/// This is how an existing whole-file override becomes a mod layer. A table is 946 KB and a weapon
/// rename touches ten strings, so shipping the file means shipping 946 KB to say a few hundred bytes
/// - and, worse, whole-file overrides are last-wins and silent, so two mods that each rename a weapon
/// cannot coexist and neither is told. Whole-file overrides of `oasisstrings.rml` are refused
/// outright, so this is how one is migrated.
/// </remarks>
public sealed class RmlFragmentsCommand : CliCommand<RmlFragmentsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<oasisstrings.rml>")]
        [Description("The edited string table to read the changes out of.")]
        public string Input { get; init; } = null!;

        [CommandOption("-b|--base <file.rml>")]
        [Description("The retail table to diff against.")]
        public string Base { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description($"Where to write the patch. Defaults to {OasisStringsPatch.FileName} beside the input.")]
        public string? Out { get; init; }

        [CommandOption("--list")]
        [Description("Report what would be written without writing it.")]
        public bool List { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken _)
    {
        IReadOnlyList<OasisStringEdit> changed = OasisStringsPatch.Changed(
            StringTableContainerSplitter.Strings(RmlDocument.Deserialize(CliIO.ReadInput(settings.Input))),
            StringTableContainerSplitter.Strings(RmlDocument.Deserialize(CliIO.ReadInput(settings.Base))));

        AnsiConsole.MarkupLine(
            $"[grey]{settings.Input.EscapeMarkup()}[/]: [green]{changed.Count} string(s) differ from "
            + $"vanilla[/], in {changed.Select(e => e.Section).Distinct().Count()} section(s)");

        if (changed.Count == 0)
        {
            AnsiConsole.MarkupLine("  [yellow]nothing to stage - this table matches the base[/]");
            return 0;
        }

        foreach (OasisStringEdit edit in changed)
        {
            AnsiConsole.MarkupLine(
                $"    {edit.Section.EscapeMarkup()};{edit.Key.EscapeMarkup()}  "
                + $"[grey]{Summarize(edit.Value).EscapeMarkup()}[/]");
        }

        if (settings.List)
        {
            return 0;
        }

        string document = OasisStringsPatch.Render(changed);
        string outPath = settings.Out
            ?? Path.Combine(Path.GetDirectoryName(settings.Input) ?? ".", OasisStringsPatch.FileName);
        CliIO.WriteOutput(outPath, document);

        AnsiConsole.MarkupLine(
            $"  wrote [green]{changed.Count}[/] edit(s) ({document.Length:N0} B) to "
            + $"[grey]{outPath.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine(
            $"  [grey]stage it as mods\\languages\\<language>\\{OasisStringsPatch.FileName}[/]");
        return 0;
    }

    /// <summary>A value short enough to list, with the newlines a paragraph carries flattened.</summary>
    private static string Summarize(string value)
    {
        string flat = value.ReplaceLineEndings(" ");
        return flat.Length <= 60 ? flat : string.Concat(flat.AsSpan(0, 57), "...");
    }
}
