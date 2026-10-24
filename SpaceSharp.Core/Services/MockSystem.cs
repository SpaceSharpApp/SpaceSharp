using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>A deterministic in-memory sample PC for demos and screenshots (<c>--mock</c>).</summary>
public static class MockSystem
{
    private const long KB = 1024, MB = KB * 1024, GB = MB * 1024, TB = GB * 1024;

    /// <summary>A mock drive.</summary>
    /// <param name="Root">The drive root with its backslash, such as "C:\".</param>
    /// <param name="Label">The volume label shown on the start screen.</param>
    /// <param name="Used">Bytes in use.</param>
    /// <param name="Total">Capacity in bytes.</param>
    /// <param name="Kind">A DriveKind name ("Nvme", "Ssd", "Hdd", "Usb") that picks the start-screen icon.</param>
    /// <param name="Format">The file system name as Windows reports it ("NTFS", "exFAT", "FAT32").</param>
    public sealed record Drive(string Root, string Label, long Used, long Total, string Kind, string Format);

    /// <summary>The mock drives; Kind is a DriveKind name.</summary>
    public static IReadOnlyList<Drive> Drives
    {
        get;
    } = new[]
    {
        new Drive(@"C:\", "Local Disk", (long)(271.37 * GB), 1 * TB, "Nvme", "NTFS"),
        new Drive(@"D:\", "Data", (long)(1.52 * TB), (long)(1.82 * TB), "Hdd", "NTFS"),
        new Drive(@"E:\", "Games", (long)(3.31 * TB), (long)(3.64 * TB), "Ssd", "NTFS"),
        new Drive(@"F:\", "Backup", (long)(2.16 * TB), (long)(3.64 * TB), "Usb", "exFAT"),
        new Drive(@"G:\", "USB Drive", (long)(12.4 * GB), (long)(57.6 * GB), "Usb", "FAT32")
    };

    /// <summary>A mock saved scan for the start screen.</summary>
    /// <param name="Info">The header as a real saved scan would carry it; its Path is empty because no file exists.</param>
    /// <param name="IsDrive">True when the scan root is a drive, which shows the fill ring and the "changed since" nudge.</param>
    /// <param name="Label">The drive's volume label; null for a folder scan.</param>
    /// <param name="Free">The drive's free bytes now, for the nudge; 0 for a folder scan.</param>
    /// <param name="Total">The drive's capacity in bytes; 0 for a folder scan.</param>
    public sealed record RecentScan(ScanFileInfo Info, bool IsDrive, string? Label, long Free, long Total);

    /// <summary>The mock saved scans, newest first.</summary>
    /// <returns>Three drive scans and one folder scan with scan times relative to now, so the ages read naturally whenever the app runs.</returns>
    public static IReadOnlyList<RecentScan> RecentScans()
    {
        DateTime now = DateTime.UtcNow;

        // filledSince is how much the drive filled after the scan, so the start screen's nudge has something to say.
        RecentScan DriveScan(Drive drive, TimeSpan ago, int files, long filledSince = 0)
        {
            var info = new ScanFileInfo(
                drive.Root,
                now - ago,
                "mock",
                drive.Total - drive.Used + filledSince,
                files,
                drive.Used - filledSince,
                string.Empty);
            return new RecentScan(info, true, drive.Label, drive.Total - drive.Used, drive.Total);
        }

        var folderScanInfo = new ScanFileInfo(
            @"C:\Users\User\Projects", now - TimeSpan.FromDays(14), "mock", 0, 184_302, (long)(38.2 * GB), string.Empty);

        return new[]
        {
            DriveScan(Drives[0], TimeSpan.FromHours(13), 2_133, (long)(14.2 * GB)),
            DriveScan(Drives[3], TimeSpan.FromDays(3), 48_211),
            DriveScan(Drives[2], TimeSpan.FromDays(7), 612_904, (long)(61.5 * GB)),
            new RecentScan(folderScanInfo, false, null, 0, 0)
        };
    }

    /// <summary>Builds the mock tree with free space, a baseline from yesterday and the items gone since.</summary>
    /// <param name="rootPath">The root the tree is named by; matched against <see cref="Drives"/> for the free space, with C: as the fallback.</param>
    /// <returns>The sorted tree with a free-space node, a header dated 13 hours ago, the same header dated a day ago as the baseline, and the gone items by folder.</returns>
    public static (FsNode Root, ScanFileInfo Info, ScanFileInfo Baseline, ScanComparison Comparison) Build(string rootPath = @"C:\")
    {
        DateTime now = DateTime.UtcNow;
        var builder = new Builder(now);
        FsNode root = builder.Tree(rootPath);
        root.SortBy(SizeMeasure.FileSize);

        var comparison = new ScanComparison();
        MarkBaseline(root, comparison);
        comparison.Sort();

        Drive drive = Drives.FirstOrDefault(candidate => string.Equals(candidate.Root, rootPath, StringComparison.OrdinalIgnoreCase))
                      ?? Drives[0];
        long free = Math.Max(0, drive.Total - root.Size);
        root.AddFreeSpace(free);

        var info = new ScanFileInfo(
            rootPath, now.AddHours(-13), "MFT, links once", free, root.FileCount, root.Size - free, string.Empty);
        ScanFileInfo baseline = info with
        {
            ScannedUtc = now.AddDays(-1)
        };
        return (root, info, baseline, comparison);
    }

    // ------------------------------------------------------------------ the tree

    /// <summary>Builds the fixed sample tree; one instance per tree so the random sequence starts over.</summary>
    private sealed class Builder
    {
        private readonly DateTime _now;
        private long _seed = 20260924;

        /// <summary>Creates a builder whose file dates are relative to the given moment.</summary>
        /// <param name="now">The reference time, UTC; files are dated months before it.</param>
        public Builder(DateTime now) => _now = now;

        /// <summary>The next value of a Lehmer sequence, 0 to 1; the fixed seed keeps every mock tree identical.</summary>
        /// <returns>A pseudo-random fraction in [0, 1); the sequence is the same on every run.</returns>
        private double NextRandom() => (_seed = _seed * 16807 % 2147483647) / 2147483647.0;

        /// <summary>Adds a file with its size rounded to whole bytes, its allocation rounded up to 4 KB and a stable date from its path.</summary>
        /// <param name="parent">The folder the file goes in; the file is appended to its children.</param>
        /// <param name="name">The file name with extension; the extension decides its category color.</param>
        /// <param name="bytes">The size in bytes, as a double so callers can write 1.3 * GB.</param>
        /// <returns>The new file node.</returns>
        private FsNode AddFile(FsNode parent, string name, double bytes)
        {
            var file = new FsNode(name, NodeKind.File, parent) { Size = (long)Math.Round(bytes) };
            file.Allocated = (file.Size + 4095) / 4096 * 4096;
            file.FileCount = 1;
            file.LastWriteUtc = _now.AddMonths(-MonthsAgo(file.FullPath)).AddDays(-3);
            parent.Children.Add(file);
            return file;
        }

        /// <summary>Adds an empty folder, to be filled by later calls.</summary>
        /// <param name="parent">The containing folder, or null for the root, which is then not attached anywhere.</param>
        /// <param name="name">The folder name, or the full root path when <paramref name="parent"/> is null.</param>
        /// <returns>The new folder node.</returns>
        private FsNode AddFolder(FsNode? parent, string name)
        {
            var folder = new FsNode(name, NodeKind.Directory, parent);
            parent?.Children.Add(folder);
            return folder;
        }

        /// <summary>Adds <paramref name="count"/> files named by the pattern ("#" is the number), skewed toward the small end.</summary>
        /// <param name="parent">The folder the files go in.</param>
        /// <param name="pattern">A name with "#" where the 1-based number goes, such as "clip_#.mp4".</param>
        /// <param name="count">How many files to add; each draws one value from the random sequence.</param>
        /// <param name="minMb">The smallest size, in megabytes.</param>
        /// <param name="maxMb">The largest size, in megabytes; most files land much nearer <paramref name="minMb"/>.</param>
        private void Many(FsNode parent, string pattern, int count, double minMb, double maxMb)
        {
            for (int i = 0; i < count; i++)
            {
                double megabytes = minMb + (maxMb - minMb) * Math.Pow(NextRandom(), 2.4);
                AddFile(parent, pattern.Replace("#", (i + 1).ToString()), megabytes * MB);
            }
        }

        /// <summary>Builds the whole sample PC: a user profile, a games library, Program Files, Windows, Windows.old and the page files.</summary>
        /// <param name="rootPath">The root node's name, normally a drive root.</param>
        /// <returns>The finished root with every folder summed; not yet sorted.</returns>
        public FsNode Tree(string rootPath)
        {
            FsNode root = AddFolder(null, rootPath);

            FsNode users = AddFolder(root, "Users");
            FsNode user = AddFolder(users, "User");
            FsNode games = AddFolder(user, "Games");
            FsNode example = AddFolder(games, "Example Game");
            FsNode exampleData = AddFolder(example, "data");
            for (int i = 1; i <= 12; i++)
            {
                AddFile(exampleData, $"level_{i:00}.pak", (1.3 + NextRandom() * 1.9) * GB);
            }

            for (int i = 1; i <= 6; i++)
            {
                AddFile(exampleData, $"cinematics_{i:00}.pak", (1.1 + NextRandom() * 0.9) * GB);
            }

            AddFile(exampleData, "intro.pak", 0.62 * GB);
            AddFile(exampleData, "ui.pak", 0.54 * GB);
            AddFile(exampleData, "scripts.pak", 0.21 * GB);
            Many(AddFolder(exampleData, "saves"), "save_#.sav", 30, 0.4, 3);
            Many(AddFolder(example, "bin"), "module_#.dll", 40, 0.1, 8);
            AddFile(example, "ExampleGame.exe", 0.2 * MB);
            FsNode sampleBuild = AddFolder(AddFolder(games, "Sample Game"), "Build");
            AddFile(sampleBuild, "game_data.pak", 3.4 * GB);
            AddFile(sampleBuild, "engine.dll", 31 * MB);
            Many(AddFolder(sampleBuild, "assets"), "asset_#.bundle", 28, 20, 180);

            FsNode recordings = AddFolder(AddFolder(user, "Videos"), "Recordings");
            AddFile(recordings, "recording_01.mp4", 4.3 * GB);
            AddFile(recordings, "recording_02.mp4", 3.2 * GB);
            AddFile(recordings, "recording_03.mp4", 2.6 * GB);
            Many(recordings, "clip_#.mp4", 14, 60, 900);
            FsNode downloads = AddFolder(user, "Downloads");
            AddFile(downloads, "os_installer.iso", 5.4 * GB);
            AddFile(downloads, "linux_image.iso", 5.7 * GB);
            AddFile(downloads, "setup.exe", 1.3 * GB);
            AddFile(downloads, "tools.zip", 0.18 * GB);
            Many(downloads, "archive_#.zip", 22, 2, 260);
            Many(downloads, "photo_#.jpg", 30, 1, 6);
            FsNode pictures = AddFolder(user, "Pictures");
            Many(AddFolder(pictures, "Screenshots"), "screenshot_#.png", 120, 0.4, 5);
            Many(AddFolder(pictures, "Camera"), "photo_#.jpg", 160, 2, 9);
            FsNode documents = AddFolder(user, "Documents");
            FsNode savedGames = AddFolder(documents, "Saved Games");
            Many(AddFolder(savedGames, "Game One"), "save_#.dat", 40, 2, 9);
            Many(AddFolder(savedGames, "Game Two"), "save_#.dat", 30, 8, 24);
            Many(documents, "document_#.docx", 18, 0.05, 2);
            Many(documents, "report_#.pdf", 12, 0.2, 6);
            FsNode projects = AddFolder(user, "Projects");
            Many(AddFolder(projects, "Project A"), "source_#.cs", 16, 0.01, 0.06);
            Many(AddFolder(projects, "Project B"), "source_#.js", 60, 0.01, 0.2);
            Many(AddFolder(projects, "Project C"), "source_#.py", 30, 0.02, 0.5);
            FsNode local = AddFolder(AddFolder(user, "AppData"), "Local");
            Many(AddFolder(local, "Shader Cache"), "cache_#.bin", 40, 8, 90);
            Many(AddFolder(local, "Temp"), "temp_#.tmp", 80, 0.1, 40);
            FsNode browserCache = AddFolder(AddFolder(AddFolder(AddFolder(local, "Web Browser"), "User Data"), "Default"), "Cache");
            Many(browserCache, "f_#", 60, 2, 40);
            Many(AddFolder(local, "Code Editor"), "index_#.dat", 40, 5, 60);

            FsNode common = AddFolder(AddFolder(root, "Games Library"), "common");
            FsNode content = AddFolder(AddFolder(common, "Game One"), "content");
            for (int i = 1; i <= 14; i++)
            {
                AddFile(content, $"content_{i:00}.pak", (1.2 + NextRandom() * 4.2) * GB);
            }
            Many(content, "expansion_#.pak", 8, 800, 3600);
            FsNode two = AddFolder(AddFolder(common, "Game Two"), "Data");
            for (int i = 1; i <= 12; i++)
            {
                AddFile(two, $"textures_{i:00}.pak", (1.4 + NextRandom() * 2.6) * GB);
            }
            AddFile(two, "meshes.pak", 3.8 * GB);
            AddFile(two, "audio.pak", 4.9 * GB);
            AddFile(two, "world.dat", 0.46 * GB);
            FsNode three = AddFolder(AddFolder(common, "Game Three"), "Data");
            AddFile(three, "world.pak", 7.9 * GB);
            AddFile(three, "shared.pak", 3.1 * GB);
            AddFile(three, "textures.pak", 4.4 * GB);
            AddFile(three, "streaming.pak", 9.8 * GB);
            AddFile(three, "models.pak", 2.7 * GB);
            Many(three, "language_#.pak", 8, 100, 900);

            FsNode programs = AddFolder(root, "Program Files");
            FsNode editor = AddFolder(programs, "Code Editor");
            Many(AddFolder(editor, "lib"), "library_#.jar", 70, 2, 60);
            Many(AddFolder(editor, "plugins"), "plugin_#.jar", 90, 0.5, 30);
            Many(AddFolder(programs, "Graphics Driver"), "driver_#.dll", 40, 5, 120);
            Many(AddFolder(AddFolder(programs, "Office Suite"), "root"), "component_#.dll", 70, 1, 60);
            Many(AddFolder(AddFolder(programs, "Runtime"), "shared"), "library_#.dll", 90, 0.1, 12);

            FsNode windows = AddFolder(root, "Windows");
            Many(AddFolder(windows, "WinSxS"), "component_#", 260, 1, 80);
            Many(AddFolder(windows, "System32"), "driver_#.sys", 160, 0.1, 40);
            Many(AddFolder(windows, "Installer"), "package_#.msi", 30, 20, 400);
            Many(AddFolder(AddFolder(windows, "SoftwareDistribution"), "Download"), "update_#.cab", 24, 30, 700);

            FsNode old = AddFolder(root, "Windows.old");
            FsNode oldWindows = AddFolder(old, "Windows");
            Many(AddFolder(oldWindows, "WinSxS"), "component_#", 120, 1, 60);
            Many(AddFolder(oldWindows, "System32"), "driver_#.sys", 80, 0.1, 30);
            Many(AddFolder(AddFolder(old, "Users"), "User"), "file_#.dat", 40, 1, 200);

            FsNode programData = AddFolder(root, "ProgramData");
            Many(AddFolder(programData, "Package Cache"), "package_#.msi", 40, 5, 150);
            Many(AddFolder(programData, "Shared Data"), "data_#.bin", 40, 1, 80);
            Many(AddFolder(AddFolder(root, "$Recycle.Bin"), "S-1-5-21"), "$R#.tmp", 25, 5, 400);
            AddFile(root, "pagefile.sys", 16 * GB);
            AddFile(root, "hiberfil.sys", 12.7 * GB);
            AddFile(root, "swapfile.sys", 256 * MB);

            Finish(root);
            return root;
        }

        /// <summary>Finishes every folder bottom-up so sizes and counts are summed.</summary>
        /// <param name="folder">The subtree root; its subfolders are finished before it.</param>
        private static void Finish(FsNode folder)
        {
            foreach (FsNode child in folder.Children)
            {
                if (child.IsDirectory)
                {
                    Finish(child);
                }
            }

            folder.FinishDirectory();
        }

        /// <summary>A stable per-path age in months, 0 to 47, weighted toward recent.</summary>
        /// <param name="path">The file's full path; the same path always gives the same age.</param>
        /// <returns>Whole months, 0 to 47.</returns>
        private static int MonthsAgo(string path)
        {
            int hash = 7;
            foreach (char character in path)
            {
                hash = unchecked(hash * 31 + character);
            }

            double fraction = ((uint)hash % 1000) / 1000.0;
            return (int)Math.Floor(Math.Pow(fraction, 1.6) * 48);
        }
    }

    // ------------------------------------------------------------------ baseline

    /// <summary>Assigns a stable baseline to every node: 12% new, 28% grew, 22% shrank, rest unchanged; a few folders also lost files.</summary>
    /// <param name="node">The subtree root; every descendant gets <see cref="FsNode.HasBaseline"/> and its baseline sizes.</param>
    /// <param name="comparison">Receives the gone items of the folders <see cref="GoneFrom"/> names.</param>
    /// <returns>The subtree's baseline size in bytes, gone items included, which becomes the folder's own baseline.</returns>
    private static long MarkBaseline(FsNode node, ScanComparison comparison)
    {
        node.HasBaseline = true;
        if (!node.IsDirectory)
        {
            long? before = BaselineOf(node);
            node.BaselineSize = before;
            node.BaselineAllocated = before is null ? null : (before.Value + 4095) / 4096 * 4096;
            return before ?? 0;
        }

        long baselineTotal = 0;
        foreach (FsNode child in node.Children)
        {
            if (child.IsReal)
            {
                baselineTotal += MarkBaseline(child, comparison);
            }
        }

        foreach (GoneEntry gone in GoneFrom(node))
        {
            comparison.Add(node, gone);
            baselineTotal += gone.Size;
        }

        node.BaselineSize = baselineTotal;
        node.BaselineAllocated = baselineTotal;
        return baselineTotal;
    }

    /// <summary>The items that were in a folder yesterday and are gone today.</summary>
    /// <param name="folder">Any folder of the mock tree; matched by name, and by parent name where the name repeats.</param>
    /// <returns>The gone entries for the six folders that have any; empty for every other folder.</returns>
    private static IEnumerable<GoneEntry> GoneFrom(FsNode folder)
    {
        static GoneEntry GoneFile(string name, double bytes) =>
            new(name, false, (long)bytes, ((long)bytes + 4095) / 4096 * 4096, 1);
        static GoneEntry GoneDir(string name, double bytes, int files) =>
            new(name, true, (long)bytes, ((long)bytes + 4095) / 4096 * 4096, files);

        switch (folder.Name)
        {
            case "Downloads":
                yield return GoneFile("old_installer.iso", 4.1 * GB);
                yield return GoneFile("archive_old.zip", 310 * MB);
                break;
            case "Download" when folder.Parent?.Name == "SoftwareDistribution":
                for (int i = 1; i <= 9; i++)
                {
                    yield return GoneFile($"update_old_{i:00}.cab", (120 + i * 95) * MB);
                }
                break;
            case "Recordings":
                yield return GoneFile("recording_00.mp4", 2.4 * GB);
                break;
            case "content" when folder.Parent?.Name == "Game One":
                yield return GoneFile("content_15.pak", 3.3 * GB);
                break;
            case "Temp":
                yield return GoneDir("installer_cache", 860 * MB, 212);
                break;
            case "Shader Cache":
                yield return GoneDir("old_driver", 1.2 * GB, 48);
                break;
        }
    }

    /// <summary>The size a file had in the baseline, decided by a hash of its name so the same name always changes the same way.</summary>
    /// <param name="file">A file node with its current <see cref="FsNode.Size"/> set.</param>
    /// <returns>Null for a file that is new since the baseline, a smaller size for one that grew, a larger size for one that shrank, or the current size.</returns>
    private static long? BaselineOf(FsNode file)
    {
        int hash = 0;
        foreach (char character in file.Name)
        {
            hash = unchecked(hash * 31 + character);
        }

        double fraction = ((uint)hash % 100) / 100.0;
        if (fraction < 0.12)
        {
            return null;   // new since the baseline
        }

        if (fraction < 0.40)
        {
            return file.Size - (long)Math.Round(file.Size * (fraction - 0.1));   // grew
        }

        if (fraction < 0.62)
        {
            return file.Size + (long)Math.Round(file.Size * (fraction - 0.4) * 0.6);   // shrank
        }

        return file.Size;
    }
}
