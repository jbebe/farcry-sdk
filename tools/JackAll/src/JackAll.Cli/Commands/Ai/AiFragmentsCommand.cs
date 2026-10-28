using System.ComponentModel;
using System.Xml.Linq;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Ai;
using JackAll.Core.Mods;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Ai;

/// <summary>
/// Writes a brain workspace out as fragments, keeping only the instances that differ from vanilla.
/// </summary>
public sealed class AiFragmentsCommand : CliCommand<AiFragmentsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The edited .ai.rml, or the .source.xml it is compiled from.")]
        public string Input { get; init; } = null!;

        [CommandOption("-b|--base <file.ai.rml>")]
        [Description("The retail brain to diff against. Without it every instance is written.")]
        public string? Base { get; init; }

        [CommandOption("-o|--out <dir>")]
        [Description("Where to write the fragments. Defaults to <file>.fragments.")]
        public string? Out { get; init; }

        [CommandOption("--list")]
        [Description("Report what would be written without writing it.")]
        public bool List { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] bytes = settings.Input.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? AiWorkspaceFile.Compile(XElement.Parse(CliIO.ReadInputText(settings.Input)))
            : CliIO.ReadInput(settings.Input);

        AiWorkspaceContainerSplitter splitter = AiWorkspaceContainerSplitter.Instance;
        IContainerTree mine = splitter.Open(bytes);
        IContainerTree? vanilla = settings.Base is null ? null : splitter.Open(CliIO.ReadInput(settings.Base));

        if (vanilla is not null && !FragmentDiff.IsExpressible(mine, vanilla))
        {
            AnsiConsole.MarkupLine(
                "[red]This brain removes or reorders an instance, or changes its root, which no fragment "
                + "carries. Ship the whole brain.[/]");
            return 1;
        }

        string name = Path.GetFileName(settings.Base ?? settings.Input).Replace(".source.xml", ".ai.rml", StringComparison.OrdinalIgnoreCase);
        return FragmentExport.Run(mine, vanilla, settings.Input, settings.Out, settings.List,
            $@"a folder named {name}\ at the brain's own path");
    }
}
