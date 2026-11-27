using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>Shortening a name from the middle.</summary>
public class MiddleEllipsisTests
{
    private static Func<string, bool> UpTo(int length) => candidate => candidate.Length <= length;

    [Fact]
    public void ATextThatFitsIsLeftAlone()
    {
        Assert.Equal("report.pdf", MiddleEllipsis.Shorten("report.pdf", UpTo(10)));
        Assert.Equal(string.Empty, MiddleEllipsis.Shorten(string.Empty, UpTo(0)));
    }

    [Fact]
    public void TheStartAndTheEndAreKept()
    {
        string shortened = MiddleEllipsis.Shorten("irwn_dit-v2-fp16-128x128-rt1200-1080.onnx.data", UpTo(21));

        Assert.Equal("irwn_dit-v….onnx.data", shortened);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(30)]
    public void TheResultIsTheLongestThatFits(int room)
    {
        const string name = "a_rather_long_file_name_with_no_spaces.tar.gz";
        string shortened = MiddleEllipsis.Shorten(name, UpTo(room));

        Assert.Equal(room, shortened.Length);
        Assert.Contains(MiddleEllipsis.Mark, shortened);
        Assert.StartsWith(name[..((room - 1 + 1) / 2)], shortened);
        Assert.EndsWith(name[^((room - 1) / 2)..], shortened);
    }

    [Fact]
    public void WithNoRoomOnlyTheMarkIsLeft()
    {
        Assert.Equal(MiddleEllipsis.Mark, MiddleEllipsis.Shorten("report.pdf", UpTo(1)));
        Assert.Equal(MiddleEllipsis.Mark, MiddleEllipsis.Shorten("report.pdf", UpTo(0)));
    }
}
