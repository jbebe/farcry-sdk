namespace JackAll.ModInstaller;

/// <summary>
/// Parsed arguments: the command words, plus every <c>--flag value</c> pair after them.
/// </summary>
/// <remarks>
/// Hand-rolled rather than Spectre.Console.Cli, which is what keeps this exe trimmable - see the
/// csproj. The grammar is deliberately tiny and positional-free: a command path, then flags. Repeated
/// flags accumulate (that's how <c>--layer</c> carries an ordered list), and a flag with no value is a
/// switch. Nothing here guesses: an unknown flag is an error rather than something silently ignored,
/// because a mod manager passing a typo'd flag must not get a successful build that skipped a mod.
/// </remarks>
internal sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; }

    private CommandLine(string command, Dictionary<string, List<string>> flags)
    {
        Command = command;
        _flags = flags;
    }

    public static CommandLine Parse(string[] args, IReadOnlySet<string> knownFlags)
    {
        var words = new List<string>();
        var flags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        int i = 0;
        for (; i < args.Length && !args[i].StartsWith('-'); i++)
        {
            words.Add(args[i]);
        }

        for (; i < args.Length; i++)
        {
            string name = args[i].TrimStart('-');
            if (!knownFlags.Contains(name))
            {
                throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
            // A value is the next token unless that token is itself a flag, which makes this a switch.
            string? value = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : null;
            if (!flags.TryGetValue(name, out List<string>? values))
            {
                flags[name] = values = [];
            }
            if (value is not null)
            {
                values.Add(value);
            }
        }

        return new CommandLine(string.Join(' ', words), flags);
    }

    public bool Has(string name) => _flags.ContainsKey(name);

    public string? Value(string name)
        => _flags.TryGetValue(name, out List<string>? v) && v.Count > 0 ? v[^1] : null;

    public IReadOnlyList<string> Values(string name)
        => _flags.TryGetValue(name, out List<string>? v) ? v : [];

    /// <summary>A flag that must be present and carry a value, with the same message shape the
    /// Spectre-based CLI's validators produce.</summary>
    public string Required(string name, string why)
        => Value(name) ?? throw new ArgumentException($"--{name} is required: {why}");
}
