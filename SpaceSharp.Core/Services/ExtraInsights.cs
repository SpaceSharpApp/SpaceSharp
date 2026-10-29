using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>What hard links save: duplicates counted once instead of in full.</summary>
/// <param name="BytesSaved">The bytes the duplicates would add if counted in full.</param>
/// <param name="LinkCount">How many duplicate entries there are.</param>
/// <param name="TopFolder">The top-level folder holding most of the savings, or null when there are none.</param>
/// <param name="TopFolderBytes">The savings inside <paramref name="TopFolder"/>.</param>
public sealed record HardLinkFacts(long BytesSaved, int LinkCount, FsNode? TopFolder, long TopFolderBytes);

/// <summary>Files too small to matter one by one, and folders with nothing in them.</summary>
/// <param name="SlackBytes">Bytes allocated beyond the files' sizes: the cluster slack, summed over every file.</param>
/// <param name="TinyFiles">Files under 4 KB.</param>
/// <param name="TinyBytes">What those files hold.</param>
/// <param name="EmptyFolders">Folders with no entries at all.</param>
/// <param name="SingleFileFolders">Folders holding exactly one file and nothing else.</param>
public sealed record SmallAndEmptyFacts(long SlackBytes, int TinyFiles, long TinyBytes, int EmptyFolders, int SingleFileFolders);

/// <summary>The deepest folder and the paths that exceed the classic Windows limit.</summary>
/// <param name="MaxDepth">Folders below the root on the deepest path.</param>
/// <param name="Deepest">The folder at that depth, or null for an empty scan.</param>
/// <param name="LongPaths">Entries whose full path is over 260 characters.</param>
/// <param name="Longest">Up to five entries with the longest paths, longest first.</param>
public sealed record DeepAndLongFacts(int MaxDepth, FsNode? Deepest, int LongPaths, IReadOnlyList<FsNode> Longest);

/// <summary>The user's Downloads folder, when it is inside the scan.</summary>
/// <param name="Folder">The folder node.</param>
/// <param name="Bytes">Its size in the current measure.</param>
/// <param name="OldestUtc">The oldest file's write time, or null when it has no files.</param>
/// <param name="Installers">Installer files (.exe, .msi, .msix, .appx) directly in it.</param>
/// <param name="InstallerBytes">What those installers take.</param>
/// <param name="ExtractedArchives">Archives directly in it that have a folder of the same name beside them.</param>
/// <param name="ExtractedBytes">What those archives take.</param>
/// <param name="Largest">Up to five largest files anywhere in it, largest first.</param>
public sealed record DownloadsFacts(FsNode Folder, long Bytes, DateTime? OldestUtc, int Installers, long InstallerBytes, int ExtractedArchives, long ExtractedBytes, IReadOnlyList<FsNode> Largest);

/// <summary>One game store's library folders.</summary>
/// <param name="Store">The store's display name: Steam, Ubisoft, Epic, GOG, Xbox, EA, Riot.</param>
/// <param name="Bytes">The libraries' size in the current measure.</param>
/// <param name="Games">How many game folders the libraries hold.</param>
/// <param name="Folders">The library folders, largest first.</param>
public sealed record GameStore(string Store, long Bytes, int Games, IReadOnlyList<FsNode> Folders);

/// <summary>Pictures, music and video by type, plus screenshot folders.</summary>
/// <param name="Images">Image bytes.</param>
/// <param name="ImageFiles">Image files.</param>
/// <param name="Audio">Audio bytes.</param>
/// <param name="AudioFiles">Audio files.</param>
/// <param name="Video">Video bytes.</param>
/// <param name="VideoFiles">Video files.</param>
/// <param name="Screenshots">Bytes in folders named Screenshots.</param>
/// <param name="ScreenshotFiles">Files in those folders.</param>
public sealed record MediaFacts(long Images, int ImageFiles, long Audio, int AudioFiles, long Video, int VideoFiles, long Screenshots, int ScreenshotFiles);

/// <summary>Developer folders that a build or an install recreates.</summary>
/// <param name="NodeModules">Bytes in node_modules folders.</param>
/// <param name="Projects">How many node_modules folders there are, one per project.</param>
/// <param name="Git">Bytes in .git folders.</param>
/// <param name="Repos">How many .git folders there are.</param>
/// <param name="BuildOutput">Bytes in build output: bin and obj beside a project file, target beside Cargo.toml or pom.xml, .vs, __pycache__, venv and .venv.</param>
/// <param name="BuildFolders">How many such folders there are.</param>
/// <param name="Largest">The largest of all these folders, up to five, largest first.</param>
public sealed record DeveloperFacts(long NodeModules, int Projects, long Git, int Repos, long BuildOutput, int BuildFolders, IReadOnlyList<FsNode> Largest);

/// <summary>Everything the extra Insights rows show, from one walk of the tree.</summary>
/// <param name="HardLinks">Hard link savings; null when the scan recorded no duplicates.</param>
/// <param name="SmallAndEmpty">Tiny files, slack and empty folders.</param>
/// <param name="DeepAndLong">Depth and path lengths.</param>
/// <param name="Downloads">The Downloads folder; null when it is not inside the scan.</param>
/// <param name="Games">Game stores found, largest first; empty when none.</param>
/// <param name="Media">Pictures, music, video and screenshots.</param>
/// <param name="Developer">node_modules, .git and build output; null when none were found.</param>
public sealed record ExtraFacts(HardLinkFacts? HardLinks, SmallAndEmptyFacts SmallAndEmpty, DeepAndLongFacts DeepAndLong, DownloadsFacts? Downloads, IReadOnlyList<GameStore> Games, MediaFacts Media, DeveloperFacts? Developer);

/// <summary>Computes the extra Insights rows (hard links, small and empty, deep and long, Downloads, games, media, developer) in one pass.</summary>
public static class ExtraInsights
{
    private const int TinyLimit = 4096;
    private const int LongPathLimit = 260;
    private const int Keep = 5;

    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" };
    private static readonly HashSet<string> ProjectFileExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".sln", ".slnx" };
    private static readonly HashSet<string> BuildFolderNames = new(StringComparer.OrdinalIgnoreCase) { ".vs", "__pycache__", "venv", ".venv" };

    /// <summary>Walks the tree once and gathers every extra row's facts.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="measure">File size or size on disk, for every byte sum.</param>
    /// <param name="downloadsPath">The user's Downloads folder; null skips that row.</param>
    /// <returns>The facts; rows with nothing to show carry null or an empty list.</returns>
    public static ExtraFacts Compute(FsNode root, SizeMeasure measure, string? downloadsPath)
    {
        var walk = new Walk(measure);
        walk.Run(root);

        FsNode? downloads = downloadsPath is null ? null : root.FindDescendant(downloadsPath);
        DownloadsFacts? downloadFacts = downloads is { IsDirectory: true } ? DescribeDownloads(downloads, measure) : null;

        return new ExtraFacts(
            walk.LinkCount == 0 ? null : new HardLinkFacts(walk.LinkBytes, walk.LinkCount, walk.TopLinkFolder, walk.TopLinkFolderBytes),
            new SmallAndEmptyFacts(walk.Slack, walk.TinyFiles, walk.TinyBytes, walk.EmptyFolders, walk.SingleFileFolders),
            new DeepAndLongFacts(walk.MaxDepth, walk.Deepest, walk.LongPaths, walk.Longest.OrderByDescending(pair => pair.Length).Select(pair => pair.Node).ToList()),
            downloadFacts,
            walk.Stores(),
            new MediaFacts(walk.Images, walk.ImageFiles, walk.Audio, walk.AudioFiles, walk.Video, walk.VideoFiles, walk.Screenshots, walk.ScreenshotFiles),
            walk.DeveloperFolders.Count == 0 ? null : walk.Developer());
    }

    /// <summary>Sizes up the Downloads folder: installers, archives already extracted, the oldest file and the largest ones.</summary>
    /// <param name="folder">The Downloads node.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns>The facts.</returns>
    private static DownloadsFacts DescribeDownloads(FsNode folder, SizeMeasure measure)
    {
        var folderNames = new HashSet<string>(folder.Children.Where(child => child.IsDirectory).Select(child => child.Name), StringComparer.OrdinalIgnoreCase);
        int installers = 0;
        long installerBytes = 0;
        int extracted = 0;
        long extractedBytes = 0;
        foreach (FsNode child in folder.Children)
        {
            if (child.IsDirectory)
            {
                continue;
            }
            string extension = child.Extension;
            if (InstallerExtensions.Contains(extension))
            {
                installers++;
                installerBytes += child.SizeFor(measure);
            }
            else if (ArchiveExtensions.Contains(extension) && folderNames.Contains(Path.GetFileNameWithoutExtension(child.Name)))
            {
                extracted++;
                extractedBytes += child.SizeFor(measure);
            }
        }

        DateTime? oldest = null;
        var largest = new List<FsNode>();
        foreach (FsNode file in folder.DescendantFiles())
        {
            if (file.LastWriteUtc > DateTime.MinValue && (oldest is null || file.LastWriteUtc < oldest))
            {
                oldest = file.LastWriteUtc;
            }
            largest.Add(file);
        }
        var top = largest.OrderByDescending(file => file.SizeFor(measure)).Take(Keep).ToList();
        return new DownloadsFacts(folder, folder.SizeFor(measure), oldest, installers, installerBytes, extracted, extractedBytes, top);
    }

    /// <summary>The walk's running totals.</summary>
    private sealed class Walk
    {
        private readonly SizeMeasure _measure;
        private readonly Dictionary<FsNode, long> _linkBytesByTop = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, (long Bytes, int Games, List<FsNode> Folders)> _stores = new(StringComparer.Ordinal);

        public long LinkBytes;
        public int LinkCount;
        public FsNode? TopLinkFolder;
        public long TopLinkFolderBytes;
        public long Slack;
        public int TinyFiles;
        public long TinyBytes;
        public int EmptyFolders;
        public int SingleFileFolders;
        public int MaxDepth;
        public FsNode? Deepest;
        public int LongPaths;
        public readonly List<(FsNode Node, int Length)> Longest = new();
        public long Images, Audio, Video, Screenshots;
        public int ImageFiles, AudioFiles, VideoFiles, ScreenshotFiles;
        public readonly List<(string Kind, FsNode Folder)> DeveloperFolders = new();

        public Walk(SizeMeasure measure)
        {
            _measure = measure;
        }

        /// <summary>Visits every real node once, depth first, tracking the depth and the path length as it goes.</summary>
        /// <param name="root">The scan root, at depth 0 and the length of its own name.</param>
        public void Run(FsNode root)
        {
            var stack = new Stack<(FsNode Node, int Depth, int PathLength, FsNode? Top, bool InScreenshots)>();
            stack.Push((root, 0, root.Name.TrimEnd('\\', '/').Length, null, false));
            while (stack.Count > 0)
            {
                (FsNode node, int depth, int pathLength, FsNode? top, bool inScreenshots) = stack.Pop();
                if (node.IsFreeSpace || node.IsGroup)
                {
                    continue;
                }

                if (!node.IsDirectory)
                {
                    VisitFile(node, pathLength, top, inScreenshots);
                    continue;
                }

                if (depth > MaxDepth)
                {
                    MaxDepth = depth;
                    Deepest = node;
                }
                if (depth > 0)
                {
                    VisitFolder(node, pathLength);
                }
                if (depth > 0 && ClaimSpecialFolder(node))
                {
                    continue;   // a library or build folder is counted whole; its insides are not walked again
                }

                bool screenshots = inScreenshots || string.Equals(node.Name, "Screenshots", StringComparison.OrdinalIgnoreCase);
                foreach (FsNode child in node.Children)
                {
                    stack.Push((child, depth + 1, pathLength + 1 + child.Name.Length, depth == 0 ? child : top, screenshots));
                }
            }

            foreach ((FsNode folder, long bytes) in _linkBytesByTop)
            {
                if (bytes > TopLinkFolderBytes)
                {
                    TopLinkFolderBytes = bytes;
                    TopLinkFolder = folder;
                }
            }
        }

        private void VisitFile(FsNode file, int pathLength, FsNode? top, bool inScreenshots)
        {
            long size = file.SizeFor(_measure);
            if (file.IsHardLinkDuplicate)
            {
                LinkCount++;
                LinkBytes += file.LinkedSize;
                if (top is not null)
                {
                    _linkBytesByTop.TryGetValue(top, out long soFar);
                    _linkBytesByTop[top] = soFar + file.LinkedSize;
                }
            }
            if (file.Allocated > file.Size)
            {
                Slack += file.Allocated - file.Size;
            }
            if (file.Size < TinyLimit)
            {
                TinyFiles++;
                TinyBytes += size;
            }
            NotePathLength(file, pathLength);

            switch (FileCategories.Categorize(file.Extension))
            {
                case FileCategory.Images:
                    Images += size;
                    ImageFiles++;
                    break;
                case FileCategory.Audio:
                    Audio += size;
                    AudioFiles++;
                    break;
                case FileCategory.Video:
                    Video += size;
                    VideoFiles++;
                    break;
            }
            if (inScreenshots)
            {
                Screenshots += size;
                ScreenshotFiles++;
            }
        }

        private void VisitFolder(FsNode folder, int pathLength)
        {
            if (folder.Children.Count == 0)
            {
                EmptyFolders++;
            }
            else if (folder.Children.Count == 1 && !folder.Children[0].IsDirectory)
            {
                SingleFileFolders++;
            }
            NotePathLength(folder, pathLength);
        }

        /// <summary>Counts a path over the limit and keeps the few longest.</summary>
        /// <param name="node">The entry.</param>
        /// <param name="pathLength">Its full path's length in characters.</param>
        private void NotePathLength(FsNode node, int pathLength)
        {
            if (pathLength <= LongPathLimit)
            {
                return;
            }
            LongPaths++;
            if (Longest.Count < Keep)
            {
                Longest.Add((node, pathLength));
                return;
            }
            int shortest = 0;
            for (int i = 1; i < Longest.Count; i++)
            {
                if (Longest[i].Length < Longest[shortest].Length)
                {
                    shortest = i;
                }
            }
            if (pathLength > Longest[shortest].Length)
            {
                Longest[shortest] = (node, pathLength);
            }
        }

        /// <summary>Recognizes a game library or a developer folder by its name and what sits beside it, and counts it whole.</summary>
        /// <param name="folder">A folder below the root.</param>
        /// <returns>True when the folder was claimed, so the walk skips its contents.</returns>
        private bool ClaimSpecialFolder(FsNode folder)
        {
            string name = folder.Name;
            FsNode? parent = folder.Parent;

            // Game stores: the library folder and how many games it holds.
            if (name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return ClaimStore("Steam", folder, CountGames(folder.Children.FirstOrDefault(child => child.Name.Equals("common", StringComparison.OrdinalIgnoreCase))));
            }
            if (name.Equals("games", StringComparison.OrdinalIgnoreCase) && parent is not null && parent.Name.Equals("Ubisoft Game Launcher", StringComparison.OrdinalIgnoreCase))
            {
                return ClaimStore("Ubisoft", folder, CountGames(folder));
            }
            if (name.Equals("Epic Games", StringComparison.OrdinalIgnoreCase) && folder.Children.Any(child => child.IsDirectory && !child.Name.Equals("Launcher", StringComparison.OrdinalIgnoreCase)))
            {
                return ClaimStore("Epic", folder, folder.Children.Count(child => child.IsDirectory && !child.Name.Equals("Launcher", StringComparison.OrdinalIgnoreCase)));
            }
            if (name.Equals("Games", StringComparison.OrdinalIgnoreCase) && parent is not null && parent.Name.Equals("GOG Galaxy", StringComparison.OrdinalIgnoreCase))
            {
                return ClaimStore("GOG", folder, CountGames(folder));
            }
            if (name.Equals("XboxGames", StringComparison.OrdinalIgnoreCase))
            {
                return ClaimStore("Xbox", folder, CountGames(folder));
            }
            if (name.Equals("EA Games", StringComparison.OrdinalIgnoreCase))
            {
                return ClaimStore("EA", folder, CountGames(folder));
            }
            if (name.Equals("Riot Games", StringComparison.OrdinalIgnoreCase))
            {
                return ClaimStore("Riot", folder, folder.Children.Count(child => child.IsDirectory && !child.Name.StartsWith("Riot Client", StringComparison.OrdinalIgnoreCase)));
            }

            // Developer folders: rebuilt by an install or a build.
            if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
            {
                DeveloperFolders.Add(("node_modules", folder));
                return true;
            }
            if (name.Equals(".git", StringComparison.Ordinal))
            {
                DeveloperFolders.Add(("git", folder));
                return true;
            }
            if (BuildFolderNames.Contains(name))
            {
                DeveloperFolders.Add(("build", folder));
                return true;
            }
            if ((name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase)) && HasSibling(folder, file => ProjectFileExtensions.Contains(file.Extension)))
            {
                DeveloperFolders.Add(("build", folder));
                return true;
            }
            if (name.Equals("target", StringComparison.OrdinalIgnoreCase) && HasSibling(folder, file => file.Name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase) || file.Name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)))
            {
                DeveloperFolders.Add(("build", folder));
                return true;
            }
            return false;
        }

        private static int CountGames(FsNode? library) => library?.Children.Count(child => child.IsDirectory) ?? 0;

        private static bool HasSibling(FsNode folder, Func<FsNode, bool> test) =>
            folder.Parent is { } parent && parent.Children.Any(sibling => !sibling.IsDirectory && test(sibling));

        private bool ClaimStore(string store, FsNode folder, int games)
        {
            _stores.TryGetValue(store, out (long Bytes, int Games, List<FsNode> Folders) entry);
            entry.Folders ??= new List<FsNode>();
            entry.Folders.Add(folder);
            _stores[store] = (entry.Bytes + folder.SizeFor(_measure), entry.Games + games, entry.Folders);
            return true;
        }

        /// <summary>The stores found, largest first.</summary>
        /// <returns>One entry per store with its libraries largest first.</returns>
        public List<GameStore> Stores() => _stores
            .Select(pair => new GameStore(pair.Key, pair.Value.Bytes, pair.Value.Games, pair.Value.Folders.OrderByDescending(folder => folder.SizeFor(_measure)).ToList()))
            .OrderByDescending(store => store.Bytes)
            .ToList();

        /// <summary>The developer totals from the folders claimed.</summary>
        /// <returns>The facts with the five largest folders.</returns>
        public DeveloperFacts Developer()
        {
            long nodeModules = 0, git = 0, build = 0;
            int projects = 0, repos = 0, buildFolders = 0;
            foreach ((string kind, FsNode folder) in DeveloperFolders)
            {
                long bytes = folder.SizeFor(_measure);
                switch (kind)
                {
                    case "node_modules":
                        nodeModules += bytes;
                        projects++;
                        break;
                    case "git":
                        git += bytes;
                        repos++;
                        break;
                    default:
                        build += bytes;
                        buildFolders++;
                        break;
                }
            }
            var largest = DeveloperFolders.Select(pair => pair.Folder).OrderByDescending(folder => folder.SizeFor(_measure)).Take(Keep).ToList();
            return new DeveloperFacts(nodeModules, projects, git, repos, build, buildFolders, largest);
        }
    }
}
