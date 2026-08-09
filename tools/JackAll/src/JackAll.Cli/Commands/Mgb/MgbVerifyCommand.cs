using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Mgb;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mgb;

/// <summary>
/// Checks that a .mgb (Magma UI package), or the XML it is built from, holds together.
/// </summary>
/// <remarks>
/// <c>mgb encode</c> already reads its own output back, which proves the bytes are loadable. This
/// goes the step further that matters for an authored package: every name it references is one it
/// declares. See <see cref="MgbVerify"/> for what that covers and what it deliberately does not.
/// </remarks>
public sealed class MgbVerifyCommand : CliCommand<MgbVerifyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("The .mgb to check, or the .xml it is built from.")]
        public string Input { get; init; } = null!;

        [CommandOption("-p|--page <NAME>")]
        [Description("Also require a Page of this name that the engine's page registry can reach. Repeatable.")]
        public string[] Pages { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] input = CliIO.ReadInput(settings.Input);

        // The magic is the discriminator rather than the extension, so a .mgb saved under another
        // name still checks as one and an XML document is never fed to the binary reader. XML is
        // re-read as text rather than decoded from these bytes so its encoding declaration counts.
        bool binary = input.Length >= 5 && "MAGMA"u8.SequenceEqual(input.AsSpan(0, 5));

        // Names live in the XML and nowhere else - the binary keeps CRC32s - so they are caught on
        // the way through and handed to the check, which is the difference between "element
        // FCSE_SLOT_03" and "element #F14488EE" in a message the author has to act on.
        var names = new MgbNameLookup();
        byte[] bytes = binary ? input : MgbXml.Encode(CliIO.ReadInputText(settings.Input), names);

        // Reading it back is what makes this meaningful for XML input: the source is only as good
        // as the package it builds, and this format has no lengths and no sentinels to catch a
        // package that writes without complaint but cannot be loaded.
        MgbPackage package = MgbPackage.Read(bytes);
        AnsiConsole.MarkupLine(
            $"[grey]{settings.Input.EscapeMarkup()}[/]: {package.Describe(bytes.Length).EscapeMarkup()}");

        MgbVerifyResult result = MgbVerify.Check(package, settings.Pages, names.Names);
        foreach (MgbFinding finding in result.Findings)
        {
            AnsiConsole.MarkupLine(
                $"  [red]![/] {finding.Where.EscapeMarkup()}: {finding.Problem.EscapeMarkup()}");
        }

        if (!result.Ok)
        {
            AnsiConsole.MarkupLine(
                $"[red]{result.Findings.Count} problem(s)[/] in {settings.Input.EscapeMarkup()}");
            return 1;
        }

        string pages = settings.Pages.Length == 0
            ? ""
            : $", and {string.Join(", ", settings.Pages).EscapeMarkup()} is a reachable page";
        AnsiConsole.MarkupLine($"[green]OK[/] - {result.ReferencesChecked} local reference(s) resolve{pages}");
        return 0;
    }
}
