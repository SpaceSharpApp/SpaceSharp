using System.IO;

namespace SpaceSharp.Util;

/// <summary>Parsed command-line arguments; unknown arguments are ignored.</summary>
/// <remarks>See <see cref="HelpText"/>; hidden flags are --mock, --rtl, --lang, --audit, --scene, --shot and --size.</remarks>
public sealed class CommandLine
{
    public string? ScanPath { get; private set; }
    public string? OpenFile { get; private set; }
    public string? CompareFile { get; private set; }
    public bool ShowHelp { get; private set; }
    /// <summary>Start on <see cref="Services.MockSystem"/>.</summary>
    public bool Mock { get; private set; }
    /// <summary>Mirror every window as a right-to-left language would, whatever the language; for testing the layout.</summary>
    public bool RightToLeft { get; private set; }
    /// <summary>Run the layout audit over every scene, both directions and two window sizes, write the report here, and exit.</summary>
    public string? Audit { get; private set; }
    /// <summary>A culture code to run in, instead of the settings' language.</summary>
    public string? Language { get; private set; }
    /// <summary>A screenshot scene to pose; implies <see cref="Mock"/>.</summary>
    public string? Scene { get; private set; }
    /// <summary>With a scene: write the window to this PNG and exit.</summary>
    public string? Shot { get; private set; }
    /// <summary>With a scene: the window's client size, e.g. "1600x900".</summary>
    public (int Width, int Height)? Size { get; private set; }

    public static CommandLine Parse(string[] args)
    {
        var c = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--help" or "-h" or "/?":
                    c.ShowHelp = true;
                    break;
                case "--mock":
                    c.Mock = true;
                    break;
                case "--rtl":
                    c.RightToLeft = true;
                    break;
                case "--audit":
                    c.Audit = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? Full(args[++i]) : string.Empty;
                    c.Mock = true;
                    break;
                case "--lang" when i + 1 < args.Length:
                    c.Language = args[++i];
                    break;
                case "--scene" when i + 1 < args.Length:
                    c.Scene = args[++i].ToLowerInvariant();
                    c.Mock = true;
                    break;
                case "--shot" when i + 1 < args.Length:
                    c.Shot = Full(args[++i]);
                    break;
                case "--size" when i + 1 < args.Length:
                    var parts = args[++i].ToLowerInvariant().Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 200 && h > 200) c.Size = (w, h);
                    break;
                case "--scan" when i + 1 < args.Length:
                    c.ScanPath = Full(args[++i]);
                    break;
                case "--open" when i + 1 < args.Length:
                    c.OpenFile = Full(args[++i]);
                    break;
                case "--compare" when i + 1 < args.Length:
                    c.CompareFile = Full(args[++i]);
                    break;
                default:
                    if (a.StartsWith('-')) break;
                    string full = Full(a);
                    if (full.EndsWith(ScanFileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) c.OpenFile ??= full;
                    else if (Directory.Exists(full)) c.ScanPath ??= full;
                    break;
            }
        }
        return c;
    }

    private const string ScanFileExtension = ".sscan";

    private static string Full(string path)
    {
        try { return Path.GetFullPath(path.Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException) { return path; }
    }

    public static string HelpText =>
        "SpaceSharp: see where your disk space went.\n\n" +
        "  SpaceSharp.exe                        start normally\n" +
        "  SpaceSharp.exe D:\\                    scan a drive or folder right away\n" +
        "  SpaceSharp.exe scan.sscan             open a saved scan\n" +
        "  SpaceSharp.exe --open scan.sscan      same, explicit\n" +
        "  SpaceSharp.exe --compare old.sscan D:\\   scan D:\\ and compare with a saved scan\n" +
        "  SpaceSharp.exe --scan D:\\             same as the bare path\n" +
        "  SpaceSharp.exe --help                 this text\n";
}
