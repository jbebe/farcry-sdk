using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Infrastructure;

/// <summary>
/// Base for every jackall format command. Runs the command body inside a uniform guard that turns any
/// thrown exception into a single red "Error: message" line and a non-zero exit code — mirroring the
/// App's own "Couldn't read this file: {ex.Message}" handling, so feeding the wrong file to a decoder
/// produces a clean one-liner rather than a stack trace. Every format class in JackAll.Core already
/// throws <see cref="InvalidDataException"/> with a specific message on a bad/unsupported file, so the
/// message alone is genuinely the useful part.
///
/// When the command's settings implement <see cref="IJsonOutputSettings"/> and <c>--json</c> was
/// given, the same failure comes out as <c>{"ok":false,"error":"…"}</c> instead — a caller parsing
/// stdout must not have to tell a red markup line apart from a JSON document.
/// </summary>
public abstract class CliCommand<TSettings> : Command<TSettings>
    where TSettings : CommandSettings
{
    protected sealed override int Execute(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return Run(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            if (settings is IJsonOutputSettings { Json: true })
            {
                JsonOutput.WriteError(ex.Message);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            }
            return 1;
        }
    }

    protected abstract int Run(TSettings settings, CancellationToken cancellationToken);
}
