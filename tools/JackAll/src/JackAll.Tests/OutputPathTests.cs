using JackAll.Core.Naming;

namespace JackAll.Tests;

public class OutputPathTests
{
    [Fact]
    public void KeepsAnOrdinaryArchivePathIntact()
    {
        Assert.Equal(
            Path.Combine("worlds", "world1", "generated", "sectors.fcb"),
            OutputPath.Relative(@"worlds\world1\generated\sectors.fcb"));
    }

    [Fact]
    public void NormalizesForwardSlashesToThisPlatform()
    {
        Assert.Equal(Path.Combine("common", "menu.mgb"), OutputPath.Relative("common/menu.mgb"));
    }

    /// <summary>The whole point of the helper: an output path is joined onto a folder the user picked,
    /// so a name carrying '..' must not be able to climb out of it.</summary>
    [Theory]
    [InlineData(@"..\..\windows\system32\evil.dll")]
    [InlineData(@"worlds\..\..\evil.dll")]
    [InlineData(@"\\worlds\.\evil.dll")]
    public void CannotEscapeTheOutputFolder(string name)
    {
        string relative = OutputPath.Relative(name);

        Assert.False(Path.IsPathRooted(relative));
        Assert.DoesNotContain("..", relative.Split(Path.DirectorySeparatorChar));
        string full = Path.GetFullPath(Path.Combine(@"C:\out", relative));
        Assert.StartsWith(@"C:\out\", full, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplacesCharactersAFilenameCannotCarry()
    {
        string relative = OutputPath.Relative("weapons\\ak<47>.xbt");

        Assert.Equal(Path.Combine("weapons", "ak_47_.xbt"), relative);
    }

    /// <summary>A name whose every segment gets dropped would otherwise leave nothing to write to —
    /// the flattened fallback still can't climb anywhere, since it's a single segment by construction.</summary>
    [Fact]
    public void FallsBackToAFlattenedNameWhenEverySegmentIsDropped()
    {
        Assert.Equal(".._..", OutputPath.Relative(@"..\.."));
    }
}
