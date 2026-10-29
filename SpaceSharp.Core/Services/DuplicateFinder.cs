using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>Files with the same contents.</summary>
/// <param name="Size">Each file's size in bytes.</param>
/// <param name="Files">The copies, at least two, in path order.</param>
/// <param name="Hash">The SHA-256 of the contents, lowercase hex, for the tooltip.</param>
public sealed record DuplicateGroup(long Size, IReadOnlyList<FsNode> Files, string Hash)
{
    /// <summary>What deleting every copy but one would free.</summary>
    public long Wasted => Size * (Files.Count - 1);
}

/// <summary>How far a duplicate search has come.</summary>
/// <param name="Stage">1 while grouping by size, 2 while comparing the first bytes, 3 while hashing whole files.</param>
/// <param name="Done">Files handled in the current stage.</param>
/// <param name="Total">Files the current stage has to handle.</param>
/// <param name="BytesRead">Bytes read from disk so far, all stages.</param>
public readonly record struct DuplicateProgress(int Stage, int Done, int Total, long BytesRead);

/// <summary>What a search found, with the numbers for the summary line.</summary>
/// <param name="Groups">The duplicate sets, most wasted bytes first.</param>
/// <param name="Candidates">Files that shared a size with another and had to be read.</param>
/// <param name="BytesRead">Bytes read from disk to decide.</param>
public sealed record DuplicateResult(IReadOnlyList<DuplicateGroup> Groups, int Candidates, long BytesRead);

/// <summary>Finds files with identical contents: same size first, then the same first 16 KB, then the same SHA-256 of the whole file.</summary>
/// <remarks>Only files that share a size with another are ever opened, and most of those part ways after the first 16 KB, so a drive with few duplicates reads little. Files are read on several threads at once, since opening a file costs more than reading its first bytes and the drive queues the reads anyway.</remarks>
public static class DuplicateFinder
{
    private const int HeadLength = 16 * 1024;
    private const int BufferLength = 1024 * 1024;
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>Searches a tree for duplicate files.</summary>
    /// <param name="root">The scan root; hard-link duplicates and pseudo nodes are skipped, since a hard link is the same file.</param>
    /// <param name="minimumSize">Files smaller than this are ignored; 0 considers every file, which makes the search slow on a drive with many tiny files.</param>
    /// <param name="progress">Told about ten times a second while files are read; may be null.</param>
    /// <param name="token">Stops the search between files.</param>
    /// <returns>The groups found, biggest waste first; files that could not be read are left out of their group.</returns>
    public static DuplicateResult Find(FsNode root, long minimumSize, IProgress<DuplicateProgress>? progress, CancellationToken token)
    {
        // Stage 1: only a size shared by two or more files can hold a duplicate.
        var bySize = new Dictionary<long, List<FsNode>>();
        foreach (FsNode file in root.DescendantFiles())
        {
            if (file.IsHardLinkDuplicate || !file.IsReal || file.Size < minimumSize || file.Size == 0)
            {
                continue;
            }
            if (!bySize.TryGetValue(file.Size, out List<FsNode>? sameSize))
            {
                sameSize = new List<FsNode>();
                bySize[file.Size] = sameSize;
            }
            sameSize.Add(file);
        }
        var candidates = bySize.Values.Where(group => group.Count > 1).SelectMany(group => group).ToList();
        var reporter = new Reporter(progress);
        reporter.Report(1, candidates.Count, candidates.Count, force: true);
        token.ThrowIfCancellationRequested();

        // Stage 2: the first 16 KB splits most same-size groups apart cheaply. The key is the size and the head hash together.
        var heads = new ConcurrentDictionary<FsNode, string?>();
        ReadAll(candidates, 2, reporter, file =>
        {
            string? head = HashHead(file.FullPath, out long read);
            heads[file] = head;
            return read;
        }, token);
        var sameHead = candidates
            .Where(file => heads[file] is not null)
            .GroupBy(file => (file.Size, heads[file]!))
            .Where(group => group.Count() > 1)
            .ToList();

        // Stage 3: the whole file settles it; a file no longer than the head is already fully hashed.
        var toHash = sameHead.SelectMany(group => group).Where(file => file.Size > HeadLength).ToList();
        var hashes = new ConcurrentDictionary<FsNode, string?>();
        ReadAll(toHash, 3, reporter, file =>
        {
            string? hash = HashWhole(file.FullPath, out long read, token);
            hashes[file] = hash;
            return read;
        }, token);

        var groups = new List<DuplicateGroup>();
        foreach (IGrouping<(long Size, string Head), FsNode> group in sameHead)
        {
            IEnumerable<IGrouping<string, FsNode>> byHash = group
                .Select(file => (File: file, Hash: file.Size > HeadLength ? hashes[file] : group.Key.Head))
                .Where(entry => entry.Hash is not null)
                .GroupBy(entry => entry.Hash!, entry => entry.File);
            foreach (IGrouping<string, FsNode> same in byHash)
            {
                if (same.Count() > 1)
                {
                    groups.Add(new DuplicateGroup(group.Key.Size, same.OrderBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase).ToList(), same.Key));
                }
            }
        }

        return new DuplicateResult(groups.OrderByDescending(group => group.Wasted).ToList(), candidates.Count, reporter.BytesRead);
    }

    /// <summary>Runs a read over every file on a few threads at once, counting what was read and reporting progress.</summary>
    /// <param name="files">The files to read.</param>
    /// <param name="stage">The stage number for the reports.</param>
    /// <param name="reporter">Where progress goes.</param>
    /// <param name="read">Reads one file and returns how many bytes it read.</param>
    /// <param name="token">Stops the loop between files.</param>
    private static void ReadAll(List<FsNode> files, int stage, Reporter reporter, Func<FsNode, long> read, CancellationToken token)
    {
        reporter.Report(stage, 0, files.Count, force: true);
        if (files.Count == 0)
        {
            return;
        }
        int done = 0;
        var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) };
        try
        {
            Parallel.ForEach(files, options, file =>
            {
                long bytes = read(file);
                reporter.Add(bytes);
                reporter.Report(stage, Interlocked.Increment(ref done), files.Count, force: false);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            throw new OperationCanceledException(token);
        }
        reporter.Report(stage, files.Count, files.Count, force: true);
    }

    /// <summary>The SHA-256 of a file's first 16 KB.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytesRead">How many bytes were read; 0 when the file could not be opened.</param>
    /// <returns>The hash as lowercase hex, or null when the file could not be read.</returns>
    private static string? HashHead(string path, out long bytesRead)
    {
        bytesRead = 0;
        try
        {
            using SafeFileHandle handle = Open(path);
            byte[] buffer = new byte[HeadLength];
            int total = 0;
            int read;
            while (total < buffer.Length && (read = RandomAccess.Read(handle, buffer.AsSpan(total), total)) > 0)
            {
                total += read;
            }
            bytesRead = total;
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, total))).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The SHA-256 of a whole file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytesRead">How many bytes this call read, also when it gave up part way.</param>
    /// <param name="token">Stops the read between buffers.</param>
    /// <returns>The hash as lowercase hex, or null when the file could not be read.</returns>
    private static string? HashWhole(string path, out long bytesRead, CancellationToken token)
    {
        bytesRead = 0;
        try
        {
            using SafeFileHandle handle = Open(path);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[BufferLength];
            int read;
            while ((read = RandomAccess.Read(handle, buffer, bytesRead)) > 0)
            {
                token.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                bytesRead += read;
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Opens a file for reading, sharing it with whatever else has it open, without a stream or its buffer.</summary>
    /// <param name="path">The file; given the long-path prefix on Windows so deep paths open too.</param>
    /// <returns>The handle.</returns>
    private static SafeFileHandle Open(string path)
    {
        string target = OperatingSystem.IsWindows() && !path.StartsWith(@"\\?\", StringComparison.Ordinal) ? @"\\?\" + path : path;
        return File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
    }

    /// <summary>Hands progress on at most every 100 ms, since one report per file would swamp the UI thread, and keeps the byte count.</summary>
    private sealed class Reporter
    {
        private readonly IProgress<DuplicateProgress>? _progress;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();
        private long _bytesRead;
        private TimeSpan _last = TimeSpan.MinValue;

        /// <summary>Wraps the sink.</summary>
        /// <param name="progress">Where reports go; null drops them.</param>
        public Reporter(IProgress<DuplicateProgress>? progress)
        {
            _progress = progress;
        }

        /// <summary>Bytes read so far, all threads.</summary>
        public long BytesRead => Interlocked.Read(ref _bytesRead);

        /// <summary>Counts bytes a read returned.</summary>
        /// <param name="bytes">How many.</param>
        public void Add(long bytes) => Interlocked.Add(ref _bytesRead, bytes);

        /// <summary>Reports, unless the last report was under 100 ms ago and this one is not forced.</summary>
        /// <param name="stage">The stage number.</param>
        /// <param name="done">Files handled in the stage.</param>
        /// <param name="total">Files the stage has.</param>
        /// <param name="force">True reports regardless of the clock, for a stage's first and last report.</param>
        public void Report(int stage, int done, int total, bool force)
        {
            if (_progress is null)
            {
                return;
            }
            lock (_gate)
            {
                TimeSpan now = _clock.Elapsed;
                if (!force && now - _last < ReportEvery)
                {
                    return;
                }
                _last = now;
            }
            _progress.Report(new DuplicateProgress(stage, done, total, BytesRead));
        }
    }
}
