using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>What a set of duplicates says about itself, for the list and its filters.</summary>
public class DuplicateGroupTests
{
    private static (DuplicateGroup Mixed, DuplicateGroup Programs, DuplicateGroup Own) Sample()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode programs = TestTree.Dir("Program Files", root);
        FsNode tool = TestTree.Dir("Tool", programs);
        FsNode data = TestTree.Dir("ProgramData", root);
        FsNode downloads = TestTree.Dir("Downloads", root);
        FsNode backup = TestTree.Dir("Backup", root);
        FsNode inTool = TestTree.File(tool, "engine.dll", 500);
        FsNode inData = TestTree.File(data, "engine.dll", 500);
        FsNode inDownloads = TestTree.File(downloads, "Engine copy.dll", 500);
        FsNode photo = TestTree.File(downloads, "photo.jpg", 90);
        FsNode photoBackup = TestTree.File(backup, "photo.jpg", 90);
        TestTree.Finish(root);
        return (
            new DuplicateGroup(500, new[] { inDownloads, inTool, inData }, "aa"),
            new DuplicateGroup(500, new[] { inTool, inData }, "bb"),
            new DuplicateGroup(90, new[] { photo, photoBackup }, "cc"));
    }

    [Fact]
    public void NameIsTheFirstCopysFileName()
    {
        Assert.Equal("Engine copy.dll", Sample().Mixed.Name);
        Assert.Equal("photo.jpg", Sample().Own.Name);
    }

    [Fact]
    public void WastedIsEveryCopyButOne()
    {
        Assert.Equal(1000, Sample().Mixed.Wasted);
        Assert.Equal(90, Sample().Own.Wasted);
    }

    [Fact]
    public void AllInProgramPlacesNeedsEveryCopyThere()
    {
        (DuplicateGroup mixed, DuplicateGroup programs, DuplicateGroup own) = Sample();
        Assert.True(programs.AllInProgramPlaces);
        Assert.False(mixed.AllInProgramPlaces);
        Assert.False(own.AllInProgramPlaces);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("engine", true)]
    [InlineData("ENGINE COPY", true)]
    [InlineData("downloads", true)]
    [InlineData(@"Program Files\Tool", true)]
    [InlineData("photo", false)]
    [InlineData("Backup", false)]
    public void MentionsLooksInEveryCopysPathWithoutCase(string text, bool expected)
    {
        Assert.Equal(expected, Sample().Mixed.Mentions(text));
    }
}
