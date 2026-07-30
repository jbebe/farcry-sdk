using JackAll.ModInstaller;

namespace JackAll.Core.Tests;

/// <summary>
/// jackall-mi parses its own arguments (no Spectre.Console, so the exe can publish trimmed - see
/// JackAll.ModInstaller.csproj), which makes this parser the one new thing in that CLI with no
/// coverage from the mod pipeline's own tests.
/// </summary>
public class CommandLineTests
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "g", "layer", "l", "from", "f", "out", "o", "name", "force", "json",
    };

    private static CommandLine Parse(params string[] args) => CommandLine.Parse(args, Flags);

    [Fact]
    public void Leading_words_become_the_command_and_flags_are_read_by_name()
    {
        CommandLine cli = Parse("mod", "status", "--game", @"C:\Games\Far Cry 2", "--json");

        Assert.Equal("mod status", cli.Command);
        Assert.Equal(@"C:\Games\Far Cry 2", cli.Value("game"));
        Assert.True(cli.Has("json"));
    }

    [Fact]
    public void A_flag_with_no_value_is_a_switch_even_immediately_before_another_flag()
    {
        CommandLine cli = Parse("mod", "build", "--force", "--game", "g");

        Assert.True(cli.Has("force"));
        Assert.Null(cli.Value("force"));
        Assert.Equal("g", cli.Value("game"));
    }

    /// <summary>The load order is the whole interface of `mod build`, so repeats must accumulate in
    /// the order given rather than the last one winning.</summary>
    [Fact]
    public void Repeated_layer_flags_accumulate_in_order()
    {
        CommandLine cli = Parse("mod", "build", "--game", "g", "--layer", "a", "--layer", "b", "--layer", "c");

        Assert.Equal(["a", "b", "c"], cli.Values("layer"));
    }

    [Fact]
    public void Short_and_long_forms_are_tracked_separately_so_a_caller_can_mix_them()
    {
        CommandLine cli = Parse("mod", "build", "-g", "game", "-l", "a", "--layer", "b");

        Assert.Equal("game", cli.Value("g"));
        Assert.Equal(["a"], cli.Values("l"));
        Assert.Equal(["b"], cli.Values("layer"));
    }

    /// <summary>A typo'd flag must not produce a successful build that quietly skipped a mod.</summary>
    [Fact]
    public void An_unknown_flag_is_rejected_rather_than_ignored()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Parse("mod", "build", "--layers", "a"));
        Assert.Contains("--layers", ex.Message);
    }

    [Fact]
    public void Required_reports_the_missing_flag_by_name()
    {
        CommandLine cli = Parse("mod", "import-legacy", "--game", "g");

        ArgumentException ex = Assert.Throws<ArgumentException>(() => cli.Required("from", "the mod's zip."));
        Assert.Contains("--from is required", ex.Message);
        Assert.Contains("the mod's zip.", ex.Message);
    }

    [Fact]
    public void Flag_names_are_case_insensitive_and_absent_flags_read_as_empty()
    {
        CommandLine cli = Parse("mod", "status", "--GAME", "g");

        Assert.Equal("g", cli.Value("game"));
        Assert.False(cli.Has("json"));
        Assert.Null(cli.Value("layer"));
        Assert.Empty(cli.Values("layer"));
    }

    [Fact]
    public void A_bare_command_with_no_flags_parses_to_just_the_command()
    {
        CommandLine cli = Parse("mod", "restore");

        Assert.Equal("mod restore", cli.Command);
        Assert.False(cli.Has("game"));
    }
}
