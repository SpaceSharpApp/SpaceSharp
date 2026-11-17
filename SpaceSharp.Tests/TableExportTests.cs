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

    [Fact]
    public void CellsHoldTheRawTextOfThePickedColumns()
    {
        FsNode root = Sample();
        FsNode docs = root.Children.First(child => child.Name == "Docs");
        var options = new ExportOptions(ExportDelimiter.Comma, ExportColumns.Name | ExportColumns.SizeBytes, Header: true, Folders: true, Limit: 0);
        IReadOnlyList<IReadOnlyList<string>> cells = TableExport.Cells(docs.Children, SizeMeasure.FileSize, options);
        Assert.Equal(2, cells.Count);
        Assert.All(cells, row => Assert.Equal(2, row.Count));
        // No quoting: the comma in the name is the cell's own text.
        Assert.Contains(cells, row => row[0] == "a,b.txt" && row[1] == "1234567");
    }

    [Fact]
    public void CellsFollowTheFoldersOptionAndTheLimit()
    {
        FsNode root = Sample();
        var filesOnly = new ExportOptions(ExportDelimiter.Comma, ExportColumns.Name, Header: true, Folders: false, Limit: 0);
        IReadOnlyList<IReadOnlyList<string>> cells = TableExport.Cells(root.Children, SizeMeasure.FileSize, filesOnly);
        Assert.Equal("big.bin", Assert.Single(cells)[0]);

        var one = new ExportOptions(ExportDelimiter.Comma, ExportColumns.Name, Header: true, Folders: true, Limit: 1);
        Assert.Single(TableExport.Cells(root.Children, SizeMeasure.FileSize, one));
    }

    [Fact]
    public void CellsAndRenderAgreeOnTheRows()
    {
        FsNode root = Sample();
        var options = new ExportOptions(ExportDelimiter.Tab, ExportColumns.Name | ExportColumns.Type, Header: false, Folders: true, Limit: 0);
        IReadOnlyList<IReadOnlyList<string>> cells = TableExport.Cells(root.Children, SizeMeasure.FileSize, options);
        string[] lines = TableExport.Render(root.Children, SizeMeasure.FileSize, options, out int rows).TrimEnd().Split(Environment.NewLine);
        Assert.Equal(rows, cells.Count);
        Assert.Equal(lines, cells.Select(row => string.Join("\t", row)).ToArray());
    }

    [Fact]
    public void ColumnsOfKeepsTheWrittenOrder()
    {
        var options = new ExportOptions(ExportDelimiter.Comma, ExportColumns.Modified | ExportColumns.Path | ExportColumns.Share, Header: true, Folders: true, Limit: 0);
        Assert.Equal(new[] { ExportColumns.Path, ExportColumns.Share, ExportColumns.Modified }, TableExport.ColumnsOf(options));
    }

    [Theory]
    [InlineData(ExportColumns.SizeBytes, true)]
    [InlineData(ExportColumns.AllocatedBytes, true)]
    [InlineData(ExportColumns.Size, true)]
    [InlineData(ExportColumns.Share, true)]
    [InlineData(ExportColumns.Change, true)]
    [InlineData(ExportColumns.Path, false)]
    [InlineData(ExportColumns.Name, false)]
    [InlineData(ExportColumns.Folder, false)]
    [InlineData(ExportColumns.Type, false)]
    [InlineData(ExportColumns.Modified, false)]
    public void NumberColumnsAreTheOnesThatCount(ExportColumns column, bool expected)
    {
        Assert.Equal(expected, TableExport.IsNumber(column));
    }

    [Theory]
    [InlineData("C: 2026-11-16", "C 2026-11-16")]
    [InlineData("Program Files all 2026-11-16", "Program Files all 2026-11-16")]
    [InlineData("a<b>c|d?e*f\"g/h\\i", "abcdefghi")]
    [InlineData("  name.  ", "name")]
    [InlineData("tab\there", "tabhere")]
    [InlineData(":::", "")]
    public void FileStemDropsWhatAFileNameCannotHold(string name, string expected)
    {
        Assert.Equal(expected, TableExport.FileStem(name));
    }

    [Theory]
    [InlineData(ExportDelimiter.Comma, true, true, 0)]
    [InlineData(ExportDelimiter.Semicolon, false, true, 0)]
    [InlineData(ExportDelimiter.Tab, true, false, 0)]
    [InlineData(ExportDelimiter.Comma, true, true, 1)]
    public void ByteCountIsTheSizeOfTheWrittenFile(ExportDelimiter delimiter, bool header, bool folders, int limit)
    {
        FsNode root = Sample();
        List<FsNode> nodes = root.Children.Concat(root.Children.First(child => child.Name == "Docs").Children).ToList();
        var options = new ExportOptions(delimiter, ExportColumns.Default | ExportColumns.Share, header, folders, limit, Separators: true);
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            TableExport.Write(path, nodes, SizeMeasure.FileSize, options, scanBytes: 5000000);
            Assert.Equal(new FileInfo(path).Length, TableExport.ByteCount(nodes, SizeMeasure.FileSize, options, scanBytes: 5000000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ByteCountStopsWhenCancelled()
    {
        FsNode root = Sample();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => TableExport.ByteCount(root.Children, SizeMeasure.FileSize, ExportOptions.Default, 0, cancel.Token));
    }
}
