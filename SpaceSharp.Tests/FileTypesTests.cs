using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>The embedded file type table (Core/Resources/FileTypes.txt) and the parser that reads it.</summary>
public class FileTypesTests
{
    [Fact]
    public void TheEmbeddedTableLoadsAndIsLarge()
    {
        Assert.True(FileCategories.Known.Count > 900, $"only {FileCategories.Known.Count} extensions");
        Assert.Equal(FileCategory.Images, FileCategories.Categorize(".jpg"));
        Assert.Equal(FileCategory.Images, FileCategories.Categorize(".JPG"));
        Assert.Equal(FileCategory.Other, FileCategories.Categorize(".nothing-like-this"));
        Assert.Equal(FileCategory.Other, FileCategories.Categorize(string.Empty));
    }

    [Fact]
    public void AmbiguousExtensionsGoToTheBigFiles()
    {
        Assert.Equal(FileCategory.Video, FileCategories.Categorize(".ts"));
        Assert.Equal(FileCategory.Video, FileCategories.Categorize(".mts"));
        Assert.Equal(FileCategory.DiskImages, FileCategories.Categorize(".vhd"));
        Assert.Equal(FileCategory.Databases, FileCategories.Categorize(".mdf"));
        Assert.Equal(FileCategory.Archives, FileCategories.Categorize(".pack"));
    }

    [Fact]
    public void EveryEntryInTheTableIsLowercaseDottedAndListedOnce()
    {
        var seen = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? section = null;
        foreach (string rawLine in TableLines())
        {
            int commentStart = rawLine.IndexOf('#');
            string line = (commentStart < 0 ? rawLine : rawLine[..commentStart]).Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line[0] == '[')
            {
                section = line;
                continue;
            }
            foreach (string entry in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.True(entry.StartsWith('.'), $"{entry} has no dot");
                Assert.Equal(entry.ToLowerInvariant(), entry);
                if (!seen.TryGetValue(entry, out List<string>? sections))
                {
                    sections = new List<string>();
                    seen[entry] = sections;
                }
                sections.Add(section ?? "(none)");
            }
        }

        var duplicates = seen.Where(pair => pair.Value.Count > 1).Select(pair => $"{pair.Key} in {string.Join(" and ", pair.Value)}").ToList();
        Assert.True(duplicates.Count == 0, "listed twice: " + string.Join("; ", duplicates));
        Assert.Equal(seen.Count, FileCategories.Known.Count);
    }

    [Fact]
    public void TheParserReadsHeadingsCommentsAndSpacing()
    {
        const string table = """
            # a comment line
            [Images]
            .jpg .png   # trailing comment
              .Gif
            [Code]
            .cs
            .png
            """;
        Dictionary<string, FileCategory> map = FileCategories.ParseTable(new StringReader(table));

        Assert.Equal(FileCategory.Images, map[".jpg"]);
        Assert.Equal(FileCategory.Images, map[".GIF"]);
        Assert.Equal(FileCategory.Code, map[".cs"]);
        Assert.Equal(FileCategory.Code, map[".png"]);   // the last listing wins
        Assert.Equal(4, map.Count);
    }

    [Theory]
    [InlineData("[Pictures]\n.jpg")]
    [InlineData("[Other]\n.jpg")]
    [InlineData(".jpg\n[Images]")]
    [InlineData("[Images]\njpg")]
    public void TheParserRejectsABrokenTable(string table)
    {
        Assert.Throws<InvalidDataException>(() => FileCategories.ParseTable(new StringReader(table)));
    }

    /// <summary>The embedded table's lines, as shipped in the Core assembly.</summary>
    /// <returns>Every line of Resources/FileTypes.txt.</returns>
    private static IEnumerable<string> TableLines()
    {
        using Stream? stream = typeof(FileCategories).Assembly.GetManifestResourceStream(FileCategories.TableResource);
        if (stream is null)
        {
            throw new InvalidOperationException("The file type table is not embedded.");
        }
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
