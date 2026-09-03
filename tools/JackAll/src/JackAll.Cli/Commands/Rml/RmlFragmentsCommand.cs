using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Mods;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Rml;

/// <summary>
/// Writes a string table out as fragments, keeping only the sections that differ from vanilla.
/// </summary>
/// <remarks>
/// A table is 946 KB and a weapon rename touches ten strings in five sections, so this is what turns
/// a localization mod from a whole-file override into a diff a person can read. Whole-file overrides
/// of `oasisstrings.rml` are refused outright, so this is how one is migrated.
/// </remarks>
public sealed class RmlFragmentsCommand : CliCommand<RmlFragmentsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<oasisstrings.rml>")]
        [Description("The string table to split.")]
        public string Input { get; init; } = null!;

        [CommandOption("-b|--base <file.rml>")]
        [Description("The retail table to diff against. Without it every section is written.")]
        public string? Base { get; init; }

        [CommandOption("-o|--out <dir>")]
        [Description("Where to write the fragments. Defaults to <file.rml>.fragments.")]
        public string? Out { get; init; }

        [CommandOption("--list")]
        [Description("Report what would be written without writing it.")]
        public bool List { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
        => FragmentExport.Run(
            StringTableContainerSplitter.Instance,
            settings.Input, settings.Base, settings.Out, settings.List,
            unit: "sections",
            stageUnder: $"mods\\languages\\<language>\\{StringTableContainerSplitter.FileName}\\",
            cancellationToken);
}
