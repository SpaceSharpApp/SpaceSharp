using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class FilterSpecTests
{
    private const long KB = 1024, MB = KB * 1024, GB = MB * 1024;

    [Fact]
    public void EmptyTextIsEmpty()
    {
        Assert.True(FilterSpec.Parse("").IsEmpty);
        Assert.True(FilterSpec.Parse("   ").IsEmpty);
    }

    [Fact]
    public void ParsesTheClassicCleanupQuery()
    {
        var spec = FilterSpec.Parse("videos over 500MB not touched in 2 years");

        Assert.Equal(new[] { FileCategory.Video }, spec.Types);
        Assert.Equal(500 * MB, spec.MinBytes);
        Assert.Equal(730, spec.OlderThanDays);
        Assert.Empty(spec.Words);
        Assert.Empty(spec.Patterns);
    }

    [Fact]
    public void BareExtensionBecomesAPattern()
    {
        var spec = FilterSpec.Parse(".iso");
        Assert.Equal(new[] { "*.iso" }, spec.Patterns);
        Assert.Empty(spec.Words);
    }

    [Fact]
    public void PatternsAreKeptAsWritten()
    {
        var spec = FilterSpec.Parse("*.mp4 *.mkv");
        Assert.Equal(new[] { "*.mp4", "*.mkv" }, spec.Patterns);
    }

    [Fact]
    public void PlainTextIsANameWord()
    {
        var spec = FilterSpec.Parse("report");
        Assert.Equal(new[] { "report" }, spec.Words);
    }

    [Fact]
    public void FillerWordsAreIgnored()
    {
        var spec = FilterSpec.Parse("show all the photos that are in downloads");
        Assert.Equal(new[] { FileCategory.Images }, spec.Types);
        Assert.Equal(new[] { "downloads" }, spec.Words);
    }

    [Theory]
    [InlineData(">1GB", 1 * GB)]
    [InlineData("over 500 MB", 500 * MB)]
    [InlineData("larger than 2 GB", 2 * GB)]
    [InlineData("at least 100MB", 100 * MB)]
    [InlineData(">= 10 kb", 10 * KB)]
    [InlineData("bigger than 200", 200 * MB)]     // bare number is MB
    [InlineData("over 1.5 GB", 1536 * MB)]
    [InlineData("over 1,5 GB", 1536 * MB)]        // comma decimal
    public void ParsesMinimumSizes(string text, long expected)
    {
        Assert.Equal(expected, FilterSpec.Parse(text).MinBytes);
    }

    [Theory]
    [InlineData("<10MB", 10 * MB)]
    [InlineData("under 1 GB", 1 * GB)]
    [InlineData("smaller than 500MB", 500 * MB)]
    [InlineData("at most 5 kb", 5 * KB)]
    public void ParsesMaximumSizes(string text, long expected)
    {
        Assert.Equal(expected, FilterSpec.Parse(text).MaxBytes);
    }

    [Theory]
    [InlineData("older than 2 years", 730)]
    [InlineData("not modified in 6 months", 180)]
    [InlineData("unused for 1 year", 365)]
    [InlineData("untouched for 3 weeks", 21)]
    [InlineData("over 3 years old", 1095)]
    [InlineData("older than a year", 365)]
    [InlineData("not opened in one month", 30)]
    public void ParsesAges(string text, int expectedDays)
    {
        var spec = FilterSpec.Parse(text);
        Assert.Equal(expectedDays, spec.OlderThanDays);
        Assert.Null(spec.MinBytes);
        Assert.Empty(spec.Words);
    }

    [Theory]
    [InlineData("newer than 30 days", 30)]
    [InlineData("modified in the last week", 7)]
    [InlineData("last 2 months", 60)]
    [InlineData("changed within the last 3 days", 3)]
    public void ParsesRecency(string text, int expectedDays)
    {
        var spec = FilterSpec.Parse(text);
        Assert.Equal(expectedDays, spec.NewerThanDays);
        Assert.Empty(spec.Words);
    }

    [Fact]
    public void ParsesKinds()
    {
        Assert.Equal(ItemKind.Files, FilterSpec.Parse("files").Kind);
        Assert.Equal(ItemKind.Files, FilterSpec.Parse("is:file").Kind);
        Assert.Equal(ItemKind.Folders, FilterSpec.Parse("folders").Kind);
        Assert.Equal(ItemKind.Folders, FilterSpec.Parse("is:folder").Kind);
        Assert.Equal(ItemKind.Any, FilterSpec.Parse("report").Kind);
    }

    [Fact]
    public void ParsesTypeLists()
    {
        var spec = FilterSpec.Parse("type:video,audio");
        Assert.Equal(2, spec.Types.Count);
        Assert.Contains(FileCategory.Video, spec.Types);
        Assert.Contains(FileCategory.Audio, spec.Types);
    }

    [Fact]
    public void TypeWordsAreCaseInsensitive()
    {
        Assert.Contains(FileCategory.Archives, FilterSpec.Parse("Zips").Types);
        Assert.Contains(FileCategory.Programs, FilterSpec.Parse("EXECUTABLES").Types);
    }

    [Fact]
    public void ToTextRoundTrips()
    {
        var original = FilterSpec.Parse("*.mp4 report type:video >500MB <2GB older than 1 year is:file");
        var again = FilterSpec.Parse(original.ToText());

        Assert.Equal(original.Patterns, again.Patterns);
        Assert.Equal(original.Words, again.Words);
        Assert.Equal(original.Types, again.Types);
        Assert.Equal(original.MinBytes, again.MinBytes);
        Assert.Equal(original.MaxBytes, again.MaxBytes);
        Assert.Equal(original.OlderThanDays, again.OlderThanDays);
        Assert.Equal(original.Kind, again.Kind);
    }

    [Fact]
    public void ToTextUsesTheCanonicalForm()
    {
        var spec = FilterSpec.Parse("photos larger than 10 MB not touched in 2 years folders");
        Assert.Equal("type:images >10MB older than 2 years is:folder", spec.ToText());
    }

    [Fact]
    public void DescribeReadsAsASentence()
    {
        var spec = FilterSpec.Parse("videos over 500MB not touched in 2 years");
        Assert.Equal("video, over 500 MB, untouched for 2 years", spec.Describe());
    }

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(3, "3 days")]
    [InlineData(7, "1 week")]
    [InlineData(14, "2 weeks")]
    [InlineData(30, "1 month")]
    [InlineData(180, "6 months")]
    [InlineData(365, "1 year")]
    [InlineData(730, "2 years")]
    public void PeriodPicksTheLargestWholeUnit(int days, string expected)
    {
        Assert.Equal(expected, FilterSpec.Period(days));
    }
}
