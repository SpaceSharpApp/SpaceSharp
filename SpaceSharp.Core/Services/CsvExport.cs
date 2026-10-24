using System.Globalization;
using System.IO;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>Writes nodes as CSV, UTF-8 with BOM for Excel.</summary>
/// <remarks>
/// Columns, in order: Path, Name, Type, Size (bytes), Size on disk (bytes), Size, Modified (UTC), Change since last scan (bytes).
/// Path and Name are always quoted with inner quotes doubled; the other fields never contain commas or quotes and are written bare.
/// Numbers and dates use the invariant culture so the file reads the same everywhere.
/// </remarks>
public static class CsvExport
{
    /// <summary>Writes one row per real node to a new file, overwriting any file already there.</summary>
    /// <param name="path">The destination file; its folder must exist.</param>
    /// <param name="nodes">The nodes to export, written in enumeration order; the free-space and group placeholders (<see cref="FsNode.IsReal"/> false) are skipped.</param>
    /// <param name="measure">Which size fills the formatted Size column and the Change column; the two byte columns always hold both sizes.</param>
    public static void Write(string path, IEnumerable<FsNode> nodes, SizeMeasure measure)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Path,Name,Type,Size (bytes),Size on disk (bytes),Size,Modified (UTC),Change since last scan (bytes)");

        foreach (FsNode node in nodes)
        {
            if (!node.IsReal)
            {
                continue;
            }

            string typeLabel = node.IsDirectory
                ? "Folder"
                : node.Extension.Length == 0 ? "File" : node.Extension.TrimStart('.').ToUpperInvariant();
            string modified = node.LastWriteUtc > DateTime.MinValue
                ? node.LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : string.Empty;
            string change = node.HasBaseline
                ? node.ChangeFor(measure).ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            csv.Append(Quote(node.FullPath)).Append(',')
               .Append(Quote(node.Name)).Append(',')
               .Append(typeLabel).Append(',')
               .Append(node.Size.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(node.Allocated.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(SizeFormatter.Format(node.SizeFor(measure))).Append(',')
               .Append(modified).Append(',')
               .Append(change)
               .AppendLine();
        }

        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
    }

    /// <summary>Wraps a field in quotes, doubling any quotes inside it.</summary>
    /// <param name="field">The raw text of a path or name; may contain commas, quotes or line breaks.</param>
    /// <returns>The field as a valid quoted CSV value.</returns>
    private static string Quote(string field) => "\"" + field.Replace("\"", "\"\"") + "\"";
}
