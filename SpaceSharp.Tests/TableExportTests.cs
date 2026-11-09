using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

public class TableExportTests
{
    private static FsNode Sample()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "a,b.txt", 1234567, modified: new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        TestTree.File(docs, "quote\"d.md", 50);
        TestTree.File(root, "big.bin", 5000000);
        TestTree.Finish(root);
        return root;
    }

    [Fact]
    public void CommaQuotesOnlyWhatNeedsIt()
    {
        FsNode root = Sample();
        var options = new ExportOptions(ExportDelimiter.Comma, ExportColumns.Name | ExportColumns.SizeBytes, Header: true, Folders: true, Limit: 0);
        string text = TableExport.Render(root.Children, SizeMeasure.FileSize, options, out int rows);
        string[] lines = text.TrimEnd().Split(Environment.NewLine);
        Assert.Equal(2, rows);
        Assert.Equal("Name,Size (bytes)", lines[0]);
        Assert.Contains("big.bin,5000000", lines);
        Assert.Contains("Docs,1234617", lines);
    }

    [Fact]
    public void TabNeedsNoQuotesForCommas()
    {
        FsNode root = Sample();
        FsNode docs = root.Children.First(child => child.Name == "Docs");
        var options = new ExportOptions(ExportDelimiter.Tab, ExportColumns.Name, Header: false, Folders: false, Limit: 0);
        string text = TableExport.Render(docs.Children, SizeMeasure.FileSize, options, out int rows);
        Assert.Equal(2, rows);
        Assert.Contains("a,b.txt" + Environment.NewLine, text);
        Assert.Contains("\"quote\"\"d.md\"", text);
    }

    [Fact]
    public void LimitFoldersAndSeparatorsApply()
    {
        FsNode root = Sample();
        var options = new ExportOptions(ExportDelimiter.Semicolon, ExportColumns.Name | ExportColumns.SizeBytes, Header: false, Folders: false, Limit: 1, Separators: true);
        // The invariant culture groups with commas, which is what the test run is pinned to.
        string text = TableExport.Render(root.Children, SizeMeasure.FileSize, options, out int rows);
        Assert.Equal(1, rows);
        Assert.Equal("big.bin;5,000,000", text.TrimEnd());
    }

    [Fact]
    public void ShareUsesTheScanTotal()
    {
        FsNode root = Sample();
        var options = new ExportOptions(ExportDelimiter.Comma, ExportColumns.Share, Header: false, Folders: true, Limit: 0);
        string text = TableExport.Render(root.Children, SizeMeasure.FileSize, options, out _, scanBytes: root.Size);
        Assert.Contains("80.2", text);
    }
}
