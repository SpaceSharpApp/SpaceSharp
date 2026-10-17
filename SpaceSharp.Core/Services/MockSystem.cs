using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>
/// A pretend PC for trying the app without scanning anything and for posing the README screenshots: a sample drive
/// with generic names, sizes, dates and "since last scan" changes, the same every time, so the pictures stay stable. Started with <c>--mock</c>, or Ctrl+Shift+Alt+M in the main window. Nothing in it
/// touches the disk: the tree is built in memory, the drives are a fixed list, and a rescan rebuilds the tree.
/// </summary>
public static class MockSystem
{
    private const long KB = 1024, MB = KB * 1024, GB = MB * 1024, TB = GB * 1024;

    /// <summary>A drive of the pretend PC, the way the Drives list wants it.</summary>
    public sealed record Drive(string Root, string Label, long Used, long Total, string Kind, string Format);

    /// <summary>The five drives the mockup shows. Kind is a DriveKind name (Nvme, Hdd, Ssd, Usb).</summary>
    public static IReadOnlyList<Drive> Drives { get; } = new[]
    {
        new Drive(@"C:\", "Local Disk", (long)(271.37 * GB), 1 * TB, "Nvme", "NTFS"),
        new Drive(@"D:\", "Data", (long)(1.52 * TB), (long)(1.82 * TB), "Hdd", "NTFS"),
        new Drive(@"E:\", "Games", (long)(3.31 * TB), (long)(3.64 * TB), "Ssd", "NTFS"),
        new Drive(@"F:\", "Backup", (long)(2.16 * TB), (long)(3.64 * TB), "Usb", "exFAT"),
        new Drive(@"G:\", "USB Drive", (long)(12.4 * GB), (long)(57.6 * GB), "Usb", "FAT32")
    };

    /// <summary>A saved scan the start screen lists on the pretend PC.</summary>
    public sealed record RecentScan(ScanFileInfo Info, bool IsDrive, string? Label, long Free, long Total);

    /// <summary>
    /// The start screen's saved scans: the system drive from earlier today, the backup drive from a few days ago, the
    /// nearly full games drive from last week, and a folder scan. Newest first, as the start screen wants them.
    /// </summary>
    public static IReadOnlyList<RecentScan> RecentScans()
    {
        var now = DateTime.UtcNow;
        RecentScan Of(Drive d, TimeSpan ago, int files) => new(new ScanFileInfo(d.Root, now - ago, "mock", d.Total - d.Used, files, d.Used, string.Empty), true, d.Label, d.Total - d.Used, d.Total);
        return new[]
        {
            Of(Drives[0], TimeSpan.FromHours(13), 2_133),
            Of(Drives[3], TimeSpan.FromDays(3), 48_211),
            Of(Drives[2], TimeSpan.FromDays(7), 612_904),
            new RecentScan(new ScanFileInfo(@"C:\Users\User\Projects", now - TimeSpan.FromDays(14), "mock", 0, 184_302, (long)(38.2 * GB), string.Empty), false, null, 0, 0)
        };
    }

    /// <summary>The scanned "C:\" with its free space, every node marked against a pretend scan from yesterday.</summary>
    public static (FsNode Root, ScanFileInfo Info, ScanFileInfo Baseline) Build(string rootPath = @"C:\")
    {
        var now = DateTime.UtcNow;
        var builder = new Builder(now);
        var root = builder.Tree(rootPath);
        root.SortBy(SizeMeasure.FileSize);
        MarkBaseline(root);
        var drive = Drives.FirstOrDefault(d => string.Equals(d.Root, rootPath, StringComparison.OrdinalIgnoreCase)) ?? Drives[0];
        long free = Math.Max(0, drive.Total - root.Size);
        root.AddFreeSpace(free);
        var info = new ScanFileInfo(rootPath, now.AddHours(-13), "mock", free, root.FileCount, root.Size - free, string.Empty);
        var baseline = info with { ScannedUtc = now.AddDays(-1) };
        return (root, info, baseline);
    }

    // ------------------------------------------------------------------ the tree

    private sealed class Builder
    {
        private readonly DateTime _now;
        private long _seed = 20260924;

        public Builder(DateTime now) => _now = now;

        // The mockup's generator: a Lehmer sequence, so the sizes come out the same every time.
        private double Rnd() => (_seed = _seed * 16807 % 2147483647) / 2147483647.0;

        private FsNode F(FsNode parent, string name, double bytes)
        {
            var file = new FsNode(name, NodeKind.File, parent) { Size = (long)Math.Round(bytes) };
            file.Allocated = (file.Size + 4095) / 4096 * 4096;
            file.FileCount = 1;
            file.LastWriteUtc = _now.AddMonths(-MonthsAgo(file.FullPath)).AddDays(-3);
            parent.Children.Add(file);
            return file;
        }

        private FsNode D(FsNode? parent, string name)
        {
            var dir = new FsNode(name, NodeKind.Directory, parent);
            parent?.Children.Add(dir);
            return dir;
        }

        private void Many(FsNode parent, string pattern, int count, double minMb, double maxMb)
        {
            for (int i = 0; i < count; i++) F(parent, pattern.Replace("#", (i + 1).ToString()), (minMb + (maxMb - minMb) * Math.Pow(Rnd(), 2.4)) * MB);
        }

        public FsNode Tree(string rootPath)
        {
            var root = D(null, rootPath);

            var users = D(root, "Users"); var user = D(users, "User");
            var games = D(user, "Games");
            var example = D(games, "Example Game");
            var data = D(example, "data");
            for (int i = 1; i <= 12; i++) F(data, $"level_{i:00}.pak", (1.3 + Rnd() * 1.9) * GB);
            for (int i = 1; i <= 6; i++) F(data, $"cinematics_{i:00}.pak", (1.1 + Rnd() * 0.9) * GB);
            F(data, "intro.pak", 0.62 * GB); F(data, "ui.pak", 0.54 * GB); F(data, "scripts.pak", 0.21 * GB);
            Many(D(data, "saves"), "save_#.sav", 30, 0.4, 3);
            Many(D(example, "bin"), "module_#.dll", 40, 0.1, 8);
            F(example, "ExampleGame.exe", 0.2 * MB);
            var sampleBuild = D(D(games, "Sample Game"), "Build");
            F(sampleBuild, "game_data.pak", 3.4 * GB); F(sampleBuild, "engine.dll", 31 * MB); Many(D(sampleBuild, "assets"), "asset_#.bundle", 28, 20, 180);

            var recordings = D(D(user, "Videos"), "Recordings");
            F(recordings, "recording_01.mp4", 4.3 * GB); F(recordings, "recording_02.mp4", 3.2 * GB); F(recordings, "recording_03.mp4", 2.6 * GB); Many(recordings, "clip_#.mp4", 14, 60, 900);
            var downloads = D(user, "Downloads");
            F(downloads, "os_installer.iso", 5.4 * GB); F(downloads, "linux_image.iso", 5.7 * GB); F(downloads, "setup.exe", 1.3 * GB); F(downloads, "tools.zip", 0.18 * GB);
            Many(downloads, "archive_#.zip", 22, 2, 260); Many(downloads, "photo_#.jpg", 30, 1, 6);
            var pictures = D(user, "Pictures");
            Many(D(pictures, "Screenshots"), "screenshot_#.png", 120, 0.4, 5); Many(D(pictures, "Camera"), "photo_#.jpg", 160, 2, 9);
            var documents = D(user, "Documents");
            var savedGames = D(documents, "Saved Games");
            Many(D(savedGames, "Game One"), "save_#.dat", 40, 2, 9); Many(D(savedGames, "Game Two"), "save_#.dat", 30, 8, 24);
            Many(documents, "document_#.docx", 18, 0.05, 2); Many(documents, "report_#.pdf", 12, 0.2, 6);
            var projects = D(user, "Projects");
            Many(D(projects, "Project A"), "source_#.cs", 16, 0.01, 0.06); Many(D(projects, "Project B"), "source_#.js", 60, 0.01, 0.2); Many(D(projects, "Project C"), "source_#.py", 30, 0.02, 0.5);
            var local = D(D(user, "AppData"), "Local");
            Many(D(local, "Shader Cache"), "cache_#.bin", 40, 8, 90);
            Many(D(local, "Temp"), "temp_#.tmp", 80, 0.1, 40);
            Many(D(D(D(D(local, "Web Browser"), "User Data"), "Default"), "Cache"), "f_#", 60, 2, 40);
            Many(D(local, "Code Editor"), "index_#.dat", 40, 5, 60);

            var common = D(D(root, "Games Library"), "common");
            var content = D(D(common, "Game One"), "content");
            for (int i = 1; i <= 14; i++) F(content, $"content_{i:00}.pak", (1.2 + Rnd() * 4.2) * GB);
            Many(content, "expansion_#.pak", 8, 800, 3600);
            var two = D(D(common, "Game Two"), "Data");
            for (int i = 1; i <= 12; i++) F(two, $"textures_{i:00}.pak", (1.4 + Rnd() * 2.6) * GB);
            F(two, "meshes.pak", 3.8 * GB); F(two, "audio.pak", 4.9 * GB); F(two, "world.dat", 0.46 * GB);
            var three = D(D(common, "Game Three"), "Data");
            F(three, "world.pak", 7.9 * GB); F(three, "shared.pak", 3.1 * GB); F(three, "textures.pak", 4.4 * GB); F(three, "streaming.pak", 9.8 * GB); F(three, "models.pak", 2.7 * GB);
            Many(three, "language_#.pak", 8, 100, 900);

            var programs = D(root, "Program Files");
            var editor = D(programs, "Code Editor");
            Many(D(editor, "lib"), "library_#.jar", 70, 2, 60); Many(D(editor, "plugins"), "plugin_#.jar", 90, 0.5, 30);
            Many(D(programs, "Graphics Driver"), "driver_#.dll", 40, 5, 120);
            Many(D(D(programs, "Office Suite"), "root"), "component_#.dll", 70, 1, 60);
            Many(D(D(programs, "Runtime"), "shared"), "library_#.dll", 90, 0.1, 12);

            var windows = D(root, "Windows");
            Many(D(windows, "WinSxS"), "component_#", 260, 1, 80);
            Many(D(windows, "System32"), "driver_#.sys", 160, 0.1, 40);
            Many(D(windows, "Installer"), "package_#.msi", 30, 20, 400);
            Many(D(D(windows, "SoftwareDistribution"), "Download"), "update_#.cab", 24, 30, 700);

            var old = D(root, "Windows.old");
            var oldWindows = D(old, "Windows");
            Many(D(oldWindows, "WinSxS"), "component_#", 120, 1, 60); Many(D(oldWindows, "System32"), "driver_#.sys", 80, 0.1, 30);
            Many(D(D(old, "Users"), "User"), "file_#.dat", 40, 1, 200);

            var programData = D(root, "ProgramData");
            Many(D(programData, "Package Cache"), "package_#.msi", 40, 5, 150); Many(D(programData, "Shared Data"), "data_#.bin", 40, 1, 80);
            Many(D(D(root, "$Recycle.Bin"), "S-1-5-21"), "$R#.tmp", 25, 5, 400);
            F(root, "pagefile.sys", 16 * GB); F(root, "hiberfil.sys", 12.7 * GB); F(root, "swapfile.sys", 256 * MB);

            Finish(root);
            return root;
        }

        private static void Finish(FsNode dir)
        {
            foreach (var child in dir.Children) if (child.IsDirectory) Finish(child);
            dir.FinishDirectory();
        }

        /// <summary>A pretend "last changed" per file, stable, spread over four years and weighted toward recent months (the mockup's rule).</summary>
        private static int MonthsAgo(string path)
        {
            int h = 7;
            foreach (char c in path) h = unchecked(h * 31 + c);
            double r = ((uint)h % 1000) / 1000.0;
            return (int)Math.Floor(Math.Pow(r, 1.6) * 48);
        }
    }

    // ------------------------------------------------------------------ the pretend earlier scan

    /// <summary>
    /// Marks every file against a scan from yesterday the mockup's way: by a hash of the name, 12% of files are new,
    /// 28% grew, 22% shrank and the rest are unchanged. Folders carry the sum of their files' earlier sizes.
    /// </summary>
    private static long MarkBaseline(FsNode node)
    {
        node.HasBaseline = true;
        if (!node.IsDirectory)
        {
            long? before = BaselineOf(node);
            node.BaselineSize = before;
            node.BaselineAllocated = before is null ? null : (before.Value + 4095) / 4096 * 4096;
            return before ?? 0;
        }

        long sum = 0;
        foreach (var child in node.Children) if (child.IsReal) sum += MarkBaseline(child);
        node.BaselineSize = sum;
        node.BaselineAllocated = sum;
        return sum;
    }

    private static long? BaselineOf(FsNode file)
    {
        int h = 0;
        foreach (char c in file.Name) h = unchecked(h * 31 + c);
        double r = ((uint)h % 100) / 100.0;
        if (r < 0.12) return null;                                              // new since yesterday
        if (r < 0.40) return file.Size - (long)Math.Round(file.Size * (r - 0.1));   // grew
        if (r < 0.62) return file.Size + (long)Math.Round(file.Size * (r - 0.4) * 0.6); // shrank
        return file.Size;
    }
}
