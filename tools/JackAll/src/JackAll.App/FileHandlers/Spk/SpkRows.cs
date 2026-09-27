namespace JackAll.App.FileHandlers.Spk;

/// <summary>One named value in the inspector. A set goes back through the panel's edit path, which
/// rebuilds the rows, so none of these notify.</summary>
public sealed class SpkFieldRow(string label, Func<string> get, Action<string>? set)
{
    public string Label => label;

    public string Text
    {
        get => get();
        set => set?.Invoke(value);
    }

    public bool IsReadOnly => set is null;

    public bool IsBool { get; init; }

    public bool IsChecked
    {
        get => get() == bool.TrueString;
        set => set?.Invoke(value.ToString());
    }
}

/// <summary>One choice of a random container.</summary>
public sealed class SpkChoiceRow(string label, string detail, Func<string> chance, Action<string> setChance,
    bool repeat, Action<bool> setRepeat)
{
    public string Label => label;

    public string Detail => detail;

    public string Chance
    {
        get => chance();
        set => setChance(value);
    }

    public bool Repeat
    {
        get => repeat;
        set => setRepeat(value);
    }
}

/// <summary>One child of a switch or multi-event: the id it plays and, for a switch, the value it answers.</summary>
public sealed class SpkCaseRow(Func<string> target, Action<string> setTarget, Func<string>? key, Action<string>? setKey)
{
    public string Target
    {
        get => target();
        set => setTarget(value);
    }

    public string Value
    {
        get => key?.Invoke() ?? "";
        set => setKey?.Invoke(value);
    }
}

/// <summary>One point of a rolloff or multilayer curve.</summary>
public sealed class SpkPointRow(Func<string> x, Func<string> y, Action<string, string> set)
{
    public string X
    {
        get => x();
        set => set(value, y());
    }

    public string Y
    {
        get => y();
        set => set(x(), value);
    }
}
