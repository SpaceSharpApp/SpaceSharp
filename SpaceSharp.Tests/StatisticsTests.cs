using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class StatisticsTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static FsNode Sample()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode pictures = TestTree.Dir("Pictures", root);
        FsNode deep = TestTree.Dir("Deep", TestTree.Dir("Deeper", pictures));
        TestTree.Dir("Empty", root);
        TestTree.File(pictures, "a.jpg", 3000, allocated: 4096, modified: Now.AddDays(-2));
        TestTree.File(pictures, "b.jpg", 1500, allocated: 4096, modified: Now.AddDays(-400));
        TestTree.File(deep, "movie.mp4", 5_000_000, modified: Now.AddDays(-3 * 365));
        TestTree.File(root, "empty.txt", 0, allocated: 0, modified: Now.AddHours(-1));
        TestTree.Finish(root);
        return root;
    }

    [Fact]
    public void ClassesAreDoublings()
    {
        Assert.Equal(0, StatisticsReport.ClassOf(0));
        Assert.Equal(1, StatisticsReport.ClassOf(1));
        Assert.Equal(2, StatisticsReport.ClassOf(2));
        Assert.Equal(2, StatisticsReport.ClassOf(3));
        Assert.Equal(11, StatisticsReport.ClassOf(1024));
        Assert.Equal(11, StatisticsReport.ClassOf(2047));
        Assert.Equal(1024, StatisticsReport.ClassMin(11));
        Assert.Equal(2048, StatisticsReport.ClassMax(11));
    }

    [Fact]
    public void ReportCountsTheTree()
    {
        StatisticsReport report = StatisticsReport.Compute(Sample(), SizeMeasure.FileSize, Now, null);
        Assert.Equal(4, report.Files);
        Assert.Equal(4, report.Folders);
        Assert.Equal(5_004_500, report.Bytes);
        Assert.Equal(1, report.EmptyFolders);
        Assert.Equal(1, report.ZeroByteFiles);
        Assert.Equal(3, report.TinyFiles);
        Assert.Equal(1096 + 2596, report.SlackBytes);
        Assert.Equal("movie.mp4", report.Largest!.Name);
        Assert.Equal(3, report.MaxDepth);
        Assert.Equal("Deep", report.Deepest!.Name);
        Assert.Equal(2250, report.MedianBytes);
        Assert.Equal(StatisticsReport.ClassOf(5_000_000) + 1, report.Classes.Count);
        Assert.Equal(1, report.Classes[StatisticsReport.ClassOf(2048)].Files);
        Assert.Equal(1, report.Classes[StatisticsReport.ClassOf(1024)].Files);
        Assert.Equal(3000, report.Classes[12].KindBytes[(int)FileCategory.Images]);
        Assert.Equal(FileCategory.Video, report.Kinds[0].Category);
        Assert.Equal(1, report.Ages[(int)StatAge.Today].Files);
        Assert.Equal(1, report.Ages[(int)StatAge.ThisWeek].Files);
        Assert.Equal(1, report.Ages[(int)StatAge.OneToTwoYears].Files);
        Assert.Equal(1, report.UntouchedFiles);
        Assert.Equal(".mp4", report.Extensions[0].Extension);
        Assert.Equal(3, report.ExtensionCount);
        Assert.Null(report.PreviousBytes);
    }

    [Fact]
    public void ClassFilterIsInclusiveOnBothEnds()
    {
        StatisticsReport report = StatisticsReport.Compute(Sample(), SizeMeasure.FileSize, Now, null);
        Assert.Equal("is:file <0B", StatisticsReport.ClassFilter(report.Classes[0]));
        Assert.Equal("is:file >=2KB <4095B", StatisticsReport.ClassFilter(report.Classes[12]));
        FilterSpec spec = FilterSpec.Parse(StatisticsReport.ClassFilter(report.Classes[12]));
        Assert.Equal(2048, spec.MinBytes);
        Assert.Equal(4095, spec.MaxBytes);
    }

    [Fact]
    public void ComparisonFillsThePreviousFigures()
    {
        FsNode before = TestTree.Dir(@"C:\");
        FsNode pictures = TestTree.Dir("Pictures", before);
        TestTree.File(pictures, "a.jpg", 1000);
        TestTree.File(pictures, "gone.jpg", 300);
        TestTree.Finish(before);

        FsNode now = TestTree.Dir(@"C:\");
        FsNode picturesNow = TestTree.Dir("Pictures", now);
        TestTree.File(picturesNow, "a.jpg", 3000);
        TestTree.File(picturesNow, "new.png", 10);
        TestTree.Finish(now);
        ScanComparison comparison = ScanCompare.Apply(now, before);

        StatisticsReport report = StatisticsReport.Compute(now, SizeMeasure.FileSize, Now, comparison);
        Assert.Equal(1300, report.PreviousBytes);
        Assert.Equal(2, report.PreviousFiles);
        Assert.Equal(1000, report.Classes[StatisticsReport.ClassOf(1000)].PreviousBytes);
        Assert.Equal(300, report.Classes[StatisticsReport.ClassOf(300)].PreviousBytes);
        ExtensionTotal jpg = report.Extensions.First(extension => extension.Extension == ".jpg");
        Assert.Equal(1300, jpg.PreviousBytes);
        ExtensionTotal png = report.Extensions.First(extension => extension.Extension == ".png");
        Assert.Equal(0, png.PreviousBytes);
    }

    [Fact]
    public void ClassLabelsCountWithinTheirUnit()
    {
        Assert.Equal("0", StatisticsReport.ClassLabel(0));
        Assert.Equal("1", StatisticsReport.ClassLabel(1));
        Assert.Equal("2–3", StatisticsReport.ClassLabel(2));
        Assert.Equal("512–1023", StatisticsReport.ClassLabel(10));
        Assert.Equal("1–2", StatisticsReport.ClassLabel(11));
        Assert.Equal("512–1024", StatisticsReport.ClassLabel(20));
        Assert.Equal("1–2", StatisticsReport.ClassLabel(21));
    }

    [Fact]
    public void ClassUnitsStepEveryTenClasses()
    {
        Assert.Equal("B", StatisticsReport.ClassUnit(0));
        Assert.Equal("B", StatisticsReport.ClassUnit(10));
        Assert.Equal("KB", StatisticsReport.ClassUnit(11));
        Assert.Equal("KB", StatisticsReport.ClassUnit(20));
        Assert.Equal("MB", StatisticsReport.ClassUnit(21));
        Assert.Equal("PB", StatisticsReport.ClassUnit(64));
    }

    [Fact]
    public void EveryAgeFilterParsesToFilesOnly()
    {
        foreach (StatAge age in Enum.GetValues<StatAge>())
        {
            FilterSpec spec = FilterSpec.Parse(StatisticsReport.AgeFilter(age));
            Assert.Equal(ItemKind.Files, spec.Kind);
        }
        Assert.Equal("is:file newer than 1 day", StatisticsReport.AgeFilter(StatAge.Today));
        Assert.Equal("is:file older than 5 years", StatisticsReport.AgeFilter(StatAge.Older));
    }

    [Fact]
    public void SignedSizesAndCountsWriteTheSign()
    {
        Assert.Equal("±0 B", SizeFormatter.Signed(0));
        Assert.Equal("+" + SizeFormatter.Format(2048), SizeFormatter.Signed(2048));
        Assert.Equal("−" + SizeFormatter.Format(2048), SizeFormatter.Signed(-2048));
        Assert.Equal("±0", SizeFormatter.SignedCount(0));
        Assert.StartsWith("+", SizeFormatter.SignedCount(12));
        Assert.StartsWith("−", SizeFormatter.SignedCount(-12));
    }

    [Fact]
    public void ExtensionTableListsEveryExtensionInPlainNumbers()
    {
        StatisticsReport report = StatisticsReport.Compute(Sample(), SizeMeasure.FileSize, Now, null, int.MaxValue);
        (IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows) = report.ExtensionTable();

        Assert.Equal(6, header.Count);
        Assert.Equal(report.Extensions.Count, rows.Count);
        IReadOnlyList<string> jpg = rows.First(row => row[0] == ".jpg");
        Assert.Equal("Images", jpg[1]);
        Assert.Equal("4500", jpg[2]);
        Assert.Equal("2", jpg[3]);
        Assert.Equal("2250", jpg[4]);
        Assert.Equal("0.1", jpg[5]);
        Assert.All(rows, row => Assert.Equal(header.Count, row.Count));
    }

    [Fact]
    public void ExtensionTableAddsTheChangeWhenCompared()
    {
        FsNode before = TestTree.Dir(@"C:\");
        TestTree.File(before, "a.jpg", 1000);
        TestTree.Finish(before);
        FsNode now = TestTree.Dir(@"C:\");
        TestTree.File(now, "a.jpg", 3000);
        TestTree.Finish(now);
        ScanComparison comparison = ScanCompare.Apply(now, before);

        StatisticsReport report = StatisticsReport.Compute(now, SizeMeasure.FileSize, Now, comparison, int.MaxValue);
        (IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows) = report.ExtensionTable();

        Assert.Equal(7, header.Count);
        Assert.Equal("2000", rows.First(row => row[0] == ".jpg")[6]);
    }
}
