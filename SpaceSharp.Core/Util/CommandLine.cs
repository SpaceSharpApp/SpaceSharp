using System.IO;

namespace SpaceSharp.Util;

/// <summary>Parsed command-line arguments; unknown arguments are ignored.</summary>
/// <remarks>See <see cref="HelpText"/>; hidden flags are --mock, --rtl, --lang, --audit, --scene, --shot and --size.</remarks>
public sealed class CommandLine
{
    /// <summary>The folder or drive root to scan at startup (--scan or a bare existing folder), as a full path; null to start on the start screen.</summary>
    public string? ScanPath
    {
        get; private set;
    }

    /// <summary>The saved scan to open at startup (--open or a bare existing .sscan file), as a full path; null for none.</summary>
    public string? OpenFile
    {
        get; private set;
    }

    /// <summary>The saved scan to compare the startup scan against (--compare), as a full path; null for none.</summary>
    public string? CompareFile
    {
        get; private set;
    }

    /// <summary>True for --help, -h or /?: print <see cref="HelpText"/> and exit.</summary>
    public bool ShowHelp
    {
        get; private set;
    }
    /// <summary>Start on <see cref="Services.MockSystem"/>.</summary>
    public bool Mock
    {
        get; private set;
    }
    /// <summary>Mirror every window as a right-to-left language would, whatever the language; for testing the layout.</summary>
    public bool RightToLeft
    {
        get; private set;
    }
    /// <summary>Run the layout audit over every scene, both directions and two window sizes, write the report here, and exit.</summary>
    public string? Audit
    {
        get; private set;
    }
    /// <summary>A culture code to run in, instead of the settings' language.</summary>
    public string? Language
    {
        get; private set;
    }
    /// <summary>A screenshot scene to pose; implies <see cref="Mock"/>.</summary>
    public string? Scene
    {
        get; private set;
    }
    /// <summary>With a scene: write the window to this PNG and exit.</summary>
    public string? Shot
    {
        get; private set;
    }
    /// <summary>With a scene: the window's client size, e.g. "1600x900".</summary>
    public (int Width, int Height)? Size
    {
        get; private set;
    }

    /// <summary>Reads the process arguments; flags are case-insensitive, a flag that needs a value and has none is ignored, and the first bare path of each kind wins.</summary>
    /// <param name="args">The arguments as given to Main, without the program name.</param>
    /// <returns>The parsed options; never null, with every unset option null or false.</returns>
    public static CommandLine Parse(string[] args)
    {
        var parsed = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            switch (argument.ToLowerInvariant())
            {
                case "--help" or "-h" or "/?":
                    parsed.ShowHelp = true;
                    break;
                case "--mock":
                    parsed.Mock = true;
                    break;
                case "--rtl":
                    parsed.RightToLeft = true;
                    break;
                case "--audit":
                    // The report path is optional; a following flag means none was given.
                    bool hasReportPath = i + 1 < args.Length && !args[i + 1].StartsWith('-');
                    parsed.Audit = hasReportPath ? Full(args[++i]) : string.Empty;
                    parsed.Mock = true;
                    break;
                case "--lang" when i + 1 < args.Length:
                    parsed.Language = args[++i];
                    break;
                case "--scene" when i + 1 < args.Length:
                    parsed.Scene = args[++i].ToLowerInvariant();
                    parsed.Mock = true;
                    break;
                case "--shot" when i + 1 < args.Length:
                    parsed.Shot = Full(args[++i]);
                    break;
                case "--size" when i + 1 < args.Length:
                    string[] dimensions = args[++i].ToLowerInvariant().Split('x');
                    if (dimensions.Length == 2
                        && int.TryParse(dimensions[0], out int width)
                        && int.TryParse(dimensions[1], out int height)
                        && width > 200 && height > 200)
                    {
                        parsed.Size = (width, height);
                    }
                    break;
                case "--scan" when i + 1 < args.Length:
                    parsed.ScanPath = Full(args[++i]);
                    break;
                case "--open" when i + 1 < args.Length:
                    parsed.OpenFile = Full(args[++i]);
                    break;
                case "--compare" when i + 1 < args.Length:
                    parsed.CompareFile = Full(args[++i]);
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        break;
                    }

                    // A bare argument is a saved scan or a folder, whichever exists; the first of each wins.
                    string fullPath = Full(argument);
                    if (fullPath.EndsWith(ScanFileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                    {
                        parsed.OpenFile ??= fullPath;
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        parsed.ScanPath ??= fullPath;
                    }
                    break;
            }
        }

        return parsed;
    }

    private const string ScanFileExtension = ".sscan";

    /// <summary>Resolves a path argument against the working directory, dropping surrounding quotes.</summary>
    /// <param name="path">A path as typed on the command line, possibly relative or quoted.</param>
    /// <returns>The absolute path, or the trimmed input unchanged when the runtime cannot resolve it.</returns>
    private static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // A path the runtime cannot resolve is passed through as typed.
            return path;
        }
    }

    /// <summary>The usage text for --help, listing the public options only.</summary>
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
