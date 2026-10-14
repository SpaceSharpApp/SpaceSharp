using System.Globalization;
using System.IO;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>Writes lists of files and folders as CSV (UTF-8 with BOM, so Excel opens it correctly).</summary>
public static class CsvExport
{
    public static void Write(string path, IEnumerable<FsNode> nodes, SizeMeasure measure)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Path,Name,Type,Size (bytes),Size on disk (bytes),Size,Modified (UTC),Change since last scan (bytes)");
        foreach (var n in nodes)
        {
            if (!n.IsReal) continue;
            sb.Append(Q(n.FullPath)).Append(',')
              .Append(Q(n.Name)).Append(',')
              .Append(n.IsDirectory ? "Folder" : n.Extension.Length == 0 ? "File" : n.Extension.TrimStart('.').ToUpperInvariant()).Append(',')
              .Append(n.Size.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(n.Allocated.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(SizeFormatter.Format(n.SizeFor(measure))).Append(',')
              .Append(n.LastWriteUtc > DateTime.MinValue ? n.LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty).Append(',')
              .Append(n.HasBaseline ? n.ChangeFor(measure).ToString(CultureInfo.InvariantCulture) : string.Empty)
              .AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
