namespace SpaceSharp.Tests;

public class WhatsNewTests
{
    private const string Notes = """
        Zooming no longer rearranges the map, and every item has an Inspect window.

        ## What's new

        **Stable zoom.** Each folder's layout is computed once.

        - `Rescan this folder` from the [menu](https://example.com).
        - Export to CSV.

        | File | What it is |
        |---|---|
        | `SpaceSharp.exe` | Portable |

        ## Downloads

        - this must not appear
        """;

    [Fact]
    public void StripsMarkdownAndStopsAtDownloads()
    {
        string text = UpdateWindow.Abridge(Notes)!;
        Assert.Contains("WHAT'S NEW", text);
        Assert.Contains("Stable zoom. Each folder's layout is computed once.", text);
        Assert.Contains("•  Rescan this folder from the menu.", text);
        Assert.DoesNotContain("|", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("https://", text);
        Assert.DoesNotContain("must not appear", text);
    }

    [Fact]
    public void CutsLongNotesWithAnEllipsis()
    {
        string many = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"- line {i}"));
        string text = UpdateWindow.Abridge(many, maxLines: 5)!;
        Assert.EndsWith("…", text);
        Assert.Equal(6, text.Split('\n').Length);
    }

    [Fact]
    public void EmptyNotesGiveNull()
    {
        Assert.Null(UpdateWindow.Abridge(null));
        Assert.Null(UpdateWindow.Abridge("   "));
    }

    private const string Changelog = """
        # Changelog

        ## Unreleased
        - not shipped

        ## 1.6.0
        - new icon

        ## 1.5.1
        - grouping fix

        ## 1.5.0
        - tabbed settings

        ## 1.4.0
        - translations
        """;

    [Fact]
    public void SectionsBetweenKeepsEveryReleaseAfterTheInstalledOne()
    {
        string text = UpdateWindow.SectionsBetween(Changelog, "1.4.0", "1.6.0")!;
        Assert.Contains("## 1.6.0", text);
        Assert.Contains("## 1.5.1", text);
        Assert.Contains("## 1.5.0", text);
        Assert.DoesNotContain("1.4.0", text);
        Assert.DoesNotContain("Unreleased", text);
        Assert.DoesNotContain("not shipped", text);
        Assert.True(text.IndexOf("1.6.0", StringComparison.Ordinal) < text.IndexOf("1.5.0", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionsBetweenGivesNullWhenNothingIsInRange()
    {
        Assert.Null(UpdateWindow.SectionsBetween(Changelog, "1.6.0", "1.6.0"));
        Assert.Null(UpdateWindow.SectionsBetween(Changelog, "1.6.0", "1.4.0"));
    }

    [Fact]
    public void SpansReleasesOnlyWhenMoreThanOneStepBehind()
    {
        Assert.False(UpdateWindow.SpansReleases("1.5.0", "1.5.1"));
        Assert.True(UpdateWindow.SpansReleases("1.5.0", "1.5.2"));
        Assert.True(UpdateWindow.SpansReleases("1.4.0", "1.6.0"));
        Assert.True(UpdateWindow.SpansReleases("1.5.1", "1.6.0"));
        Assert.False(UpdateWindow.SpansReleases(null, "1.6.0"));
        Assert.False(UpdateWindow.SpansReleases("unknown", "1.6.0"));
        Assert.False(UpdateWindow.SpansReleases("1.6.0", "1.6.0"));
    }
}
