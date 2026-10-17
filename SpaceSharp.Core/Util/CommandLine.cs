using System.IO;

namespace SpaceSharp.Util;

/// <summary>
/// What SpaceSharp was started with. Understood forms:
///   SpaceSharp.exe                       start normally (reopen the last scan if that setting is on)
///   SpaceSharp.exe D:\                   scan that drive or folder right away
///   SpaceSharp.exe D:\Games              same, for a folder
///   SpaceSharp.exe scan.sscan            open a saved scan
///   SpaceSharp.exe --open scan.sscan     same, explicit
///   SpaceSharp.exe --compare old.sscan D:\   scan D:\ and compare it with a saved scan
///   SpaceSharp.exe --scan D:\            same as the bare path (kept for scripts that prefer a flag)
///   SpaceSharp.exe --help                print this and exit
///   SpaceSharp.exe --mock                a pretend PC with sample data, for trying the app (not in the help text)
///   SpaceSharp.exe --scene start         the pretend PC posed as one of the README screenshots (see MainWindow.Showcase.cs)
///   SpaceSharp.exe --scene start --shot docs/shot-start.png --size 1600x900   the same, written to a PNG, then exit
/// Anything not understood is ignored rather than refused, so a stray argument never stops the app.
/// </summary>
public sealed class CommandLine
{
    public string? ScanPath { get; private set; }
    public string? OpenFile { get; private set; }
    public string? CompareFile { get; private set; }
    public bool ShowHelp { get; private set; }
    /// <summary>Start on the pretend PC (<see cref="Services.MockSystem"/>) instead of the real drives.</summary>
    public bool Mock { get; private set; }
    /// <summary>A screenshot scene to pose (implies <see cref="Mock"/>): none, drawer, insights, compare, inspect, settings, menu, start, about, update, light.</summary>
    public string? Scene { get; private set; }
    /// <summary>With a scene: write the window to this PNG and exit.</summary>
    public string? Shot { get; private set; }
    /// <summary>With a scene: the window's client size, "1600x900".</summary>
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
