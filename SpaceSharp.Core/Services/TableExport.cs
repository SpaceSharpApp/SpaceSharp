using System.Globalization;
using System.IO;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>The columns a table export can carry, in the order they are written.</summary>
[Flags]
public enum ExportColumns
{
    /// <summary>No column; an export needs at least one.</summary>
    None = 0,
    /// <summary>The full path.</summary>
    Path = 1,
    /// <summary>The file or folder name.</summary>
    Name = 2,
    /// <summary>The folder the item is in.</summary>
    Folder = 4,
    /// <summary>"Folder", or the extension in capitals, or "File" for a file without one.</summary>
    Type = 8,
    /// <summary>The size in bytes.</summary>
    SizeBytes = 16,
    /// <summary>The size on disk in bytes.</summary>
    AllocatedBytes = 32,
    /// <summary>The size in the current measure, written the way the map writes it ("1.2 GB").</summary>
    Size = 64,
    /// <summary>When the item last changed, in UTC.</summary>
    Modified = 128,
    /// <summary>The change in bytes against the compared scan; blank without one.</summary>
    Change = 256,
    /// <summary>The item's share of the whole scan, as a percentage with one decimal.</summary>
    Share = 512,
    /// <summary>What the first version of the export wrote.</summary>
    Default = Path | Name | Type | SizeBytes | AllocatedBytes | Size | Modified | Change
}

/// <summary>How the field separator is chosen.</summary>
public enum ExportDelimiter
{
    /// <summary>A comma: CSV as most tools read it.</summary>
    Comma,
    /// <summary>A semicolon: what Excel expects in the locales that use a comma for decimals.</summary>
    Semicolon,
    /// <summary>A tab: TSV, which pastes straight into a spreadsheet.</summary>
    Tab
}

/// <summary>What an export writes and how.</summary>
/// <param name="Delimiter">The field separator.</param>
/// <param name="Columns">Which columns, in their fixed order.</param>
/// <param name="Header">True writes the column names as the first line.</param>
/// <param name="Folders">True keeps folders among the rows; false writes files only.</param>
/// <param name="Limit">The most rows to write, or 0 for all of them.</param>
/// <param name="Separators">True writes the byte columns with the UI culture's digit grouping ("15,562,073,417"); false writes bare digits a spreadsheet can sum.</param>
public sealed record ExportOptions(ExportDelimiter Delimiter, ExportColumns Columns, bool Header, bool Folders, int Limit, bool Separators = false)
{
    /// <summary>The first version's behavior: comma, every column, a header, folders kept, no limit, bare digits.</summary>
    public static readonly ExportOptions Default = new(ExportDelimiter.Comma, ExportColumns.Default, true, true, 0);

    /// <summary>The file extension the delimiter calls for, without the dot.</summary>
    public string Extension => Delimiter == ExportDelimiter.Tab ? "tsv" : "csv";
}

/// <summary>Writes nodes as a delimited table, UTF-8 with BOM for Excel.</summary>
/// <remarks>Every text field is quoted, with inner quotes doubled, whenever it holds the delimiter, a quote or a line break; numbers and
/// dates use the invariant culture so the file reads the same everywhere.</remarks>
public static class TableExport
{
    /// <summary>The columns in the order they are written.</summary>
    public static readonly ExportColumns[] Order =
    {
        ExportColumns.Path, ExportColumns.Name, ExportColumns.Folder, ExportColumns.Type, ExportColumns.SizeBytes,
        ExportColumns.AllocatedBytes, ExportColumns.Size, ExportColumns.Share, ExportColumns.Modified, ExportColumns.Change
    };

    /// <summary>The header text of a column, in English as the file's readers expect.</summary>
    /// <param name="column">One flag.</param>
    /// <returns>The header.</returns>
    public static string HeaderOf(ExportColumns column) => column switch
    {
        ExportColumns.Path => "Path",
        ExportColumns.Name => "Name",
        ExportColumns.Folder => "Folder",
        ExportColumns.Type => "Type",
        ExportColumns.SizeBytes => "Size (bytes)",
        ExportColumns.AllocatedBytes => "Size on disk (bytes)",
        ExportColumns.Size => "Size",
        ExportColumns.Modified => "Modified (UTC)",
        ExportColumns.Change => "Change since last scan (bytes)",
        ExportColumns.Share => "Share of scan (%)",
        _ => string.Empty
    };

    /// <summary>Writes the rows to a new file, overwriting any file already there.</summary>
    /// <param name="path">The destination file; its folder must exist.</param>
    /// <param name="nodes">The nodes to export, written in enumeration order; the free-space and group placeholders (<see cref="FsNode.IsReal"/> false) are skipped.</param>
    /// <param name="measure">Which size fills the Size and Change columns; the two byte columns always hold both sizes.</param>
    /// <param name="options">What to write and how.</param>
    /// <param name="scanBytes">The whole scan's bytes in the measure, for the Share column; 0 leaves it blank.</param>
    /// <returns>How many rows were written, the header not counted.</returns>
    public static int Write(string path, IEnumerable<FsNode> nodes, SizeMeasure measure, ExportOptions options, long scanBytes = 0)
    {
        string text = Render(nodes, measure, options, out int rows, scanBytes);
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return rows;
    }

    /// <summary>Builds the table as text, for the file and for a preview.</summary>
    /// <param name="nodes">The nodes, in order; placeholders are skipped.</param>
    /// <param name="measure">Which size fills the Size and Change columns.</param>
    /// <param name="options">What to write and how.</param>
    /// <param name="rows">Receives how many rows were written, the header not counted.</param>
    /// <param name="scanBytes">The whole scan's bytes in the measure, for the Share column; 0 leaves it blank.</param>
    /// <returns>The whole table, lines ending in the platform's newline.</returns>
    public static string Render(IEnumerable<FsNode> nodes, SizeMeasure measure, ExportOptions options, out int rows, long scanBytes = 0)
    {
        char delimiter = options.Delimiter switch
        {
            ExportDelimiter.Semicolon => ';',
            ExportDelimiter.Tab => '\t',
            _ => ','
        };
        ExportColumns[] columns = Order.Where(column => options.Columns.HasFlag(column)).ToArray();
        var table = new StringBuilder();
        if (options.Header)
        {
            table.AppendLine(string.Join(delimiter, columns.Select(column => Field(HeaderOf(column), delimiter))));
        }

        rows = 0;
        foreach (FsNode node in nodes)
        {
            if (!node.IsReal || (!options.Folders && node.IsDirectory))
            {
                continue;
            }
            if (options.Limit > 0 && rows >= options.Limit)
            {
                break;
            }

            table.AppendLine(string.Join(delimiter, columns.Select(column => Field(Value(node, column, measure, options, scanBytes), delimiter))));
            rows++;
        }
        return table.ToString();
    }

    /// <summary>One cell's text.</summary>
    /// <param name="node">The row's node.</param>
    /// <param name="column">The column.</param>
    /// <param name="measure">Which size the Size and Change columns use.</param>
    /// <param name="options">For the digit grouping of the byte columns.</param>
    /// <param name="scanBytes">The whole scan's bytes, for Share; 0 gives a blank.</param>
    /// <returns>The raw text, not yet quoted.</returns>
    public static string Value(FsNode node, ExportColumns column, SizeMeasure measure, ExportOptions options, long scanBytes) => column switch
    {
        ExportColumns.Path => node.FullPath,
        ExportColumns.Name => node.Name,
        ExportColumns.Folder => node.Parent?.FullPath ?? string.Empty,
        ExportColumns.Type => node.IsDirectory ? "Folder" : node.Extension.Length == 0 ? "File" : node.Extension.TrimStart('.').ToUpperInvariant(),
        ExportColumns.SizeBytes => Bytes(node.Size, options),
        ExportColumns.AllocatedBytes => Bytes(node.Allocated, options),
        ExportColumns.Size => SizeFormatter.Format(node.SizeFor(measure)),
        ExportColumns.Share => scanBytes > 0 ? (100.0 * node.SizeFor(measure) / scanBytes).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty,
        ExportColumns.Modified => node.LastWriteUtc > DateTime.MinValue ? node.LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty,
        ExportColumns.Change => node.HasBaseline ? Bytes(node.ChangeFor(measure), options) : string.Empty,
        _ => string.Empty
    };

    /// <summary>A byte count, grouped in the UI culture when the options ask for it, else bare invariant digits.</summary>
    /// <param name="bytes">The count; may be negative for a change.</param>
    /// <param name="options">Whether to group.</param>
    /// <returns>The text.</returns>
    private static string Bytes(long bytes, ExportOptions options) =>
        options.Separators ? bytes.ToString("N0", CultureInfo.CurrentCulture) : bytes.ToString(CultureInfo.InvariantCulture);

    /// <summary>Quotes a field when it needs it: it holds the delimiter, a quote or a line break.</summary>
    /// <param name="text">The raw text.</param>
    /// <param name="delimiter">The field separator in use.</param>
    /// <returns>The text as a valid field.</returns>
    private static string Field(string text, char delimiter) =>
        text.Contains(delimiter) || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
}
