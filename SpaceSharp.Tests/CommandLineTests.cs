using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class CommandLineTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "spacesharp-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void NoArgumentsMeansNothing()
    {
        var c = CommandLine.Parse(Array.Empty<string>());
        Assert.Null(c.ScanPath);
        Assert.Null(c.OpenFile);
        Assert.Null(c.CompareFile);
        Assert.False(c.ShowHelp);
    }

    [Fact]
    public void AFolderScansAndAScanFileOpens()
    {
        string dir = TempDir();
        string file = Path.Combine(dir, "old.sscan");
        File.WriteAllText(file, "x");
        try
        {
            Assert.Equal(dir, CommandLine.Parse(new[] { dir }).ScanPath);
            Assert.Equal(file, CommandLine.Parse(new[] { file }).OpenFile);
            var both = CommandLine.Parse(new[] { "--compare", file, dir });
            Assert.Equal(file, both.CompareFile);
            Assert.Equal(dir, both.ScanPath);
            Assert.Equal(dir, CommandLine.Parse(new[] { "--scan", dir }).ScanPath);
            Assert.Equal(file, CommandLine.Parse(new[] { "--open", file }).OpenFile);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnknownThingsAreIgnored()
    {
        var c = CommandLine.Parse(new[] { "--nonsense", @"Z:\does\not\exist", "--scan" });
        Assert.Null(c.ScanPath);
        Assert.Null(c.OpenFile);
        Assert.False(c.ShowHelp);
        Assert.True(CommandLine.Parse(new[] { "/?" }).ShowHelp);
        Assert.True(CommandLine.Parse(new[] { "--HELP" }).ShowHelp);
    }
}
