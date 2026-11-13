using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Models;
using SpaceSharp.Util;

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
/// <param name="Stage">1 while grouping by size, 2 while comparing samples of each file, 3 while reading whole files.</param>
/// <param name="Done">Files handled in the current stage.</param>
/// <param name="Total">Files the current stage has to handle.</param>
/// <param name="BytesRead">Bytes read from disk so far, all stages.</param>
public readonly record struct DuplicateProgress(int Stage, int Done, int Total, long BytesRead);

/// <summary>What a search found, with the numbers for the summary line.</summary>
/// <param name="Groups">The duplicate sets, most wasted bytes first.</param>
/// <param name="Candidates">Files that shared a size with another and had to be read.</param>
/// <param name="BytesRead">Bytes read from disk to decide.</param>
public sealed record DuplicateResult(IReadOnlyList<DuplicateGroup> Groups, int Candidates, long BytesRead);

/// <summary>Finds files with identical contents: same size first, then the same samples (the first, middle and last 16 KB), then the same
/// SHA-256 of the whole file.</summary>
/// <remarks>
/// Only files that share a size with another are ever opened, and nearly all of those part ways at the samples: three small reads tell
/// apart files that merely start alike, such as archives and game data with a common header, which a look at the first bytes alone
/// would send on to be read in full. Whole files are read last and only as far as needed: big files that passed the samples are read
/// side by side a chunk at a time, and a file is dropped the moment its contents so far match no other, so only true copies are read
/// to the end. Files are read on several threads at once. A file whose length is no longer what the scan recorded is left out.
/// </remarks>
public static class DuplicateFinder
{
    private const int SampleLength = 16 * 1024;              // each of the three samples
    private const int WholeSampleLimit = 3 * SampleLength;   // a file this small is read whole as its sample, which settles it
    private const int ChunkLength = 4 * 1024 * 1024;         // how much of each big file is read before the set is compared again
    private const int BufferLength = 1024 * 1024;
    private const int MaxSideBySide = 64;                    // a bigger set is hashed file by file, to keep the open handles few
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

        // Stage 2: three samples split nearly every same-size group apart cheaply. The key is the size and the sample hash together.
        var samples = new ConcurrentDictionary<FsNode, string?>();
        ReadAll(candidates, 2, reporter, file =>
        {
            string? sample = HashSample(file.FullPath, file.Size, out long read);
            samples[file] = sample;
            return read;
        }, token);
        var sameSample = candidates
            .Where(file => samples[file] is not null)
            .GroupBy(file => (file.Size, samples[file]!))
            .Where(group => group.Count() > 1)
            .ToList();

        // Stage 3: the whole file settles it; a file no longer than the samples together is already fully hashed. Small files are
        // hashed one by one; big ones are read side by side within their set, so a file that turns out different is not read on.
        var hashes = new ConcurrentDictionary<FsNode, string?>();
        var oneByOne = new List<FsNode>();
        var sideBySide = new List<List<FsNode>>();
        int toRead = 0;
        foreach (IGrouping<(long Size, string Sample), FsNode> group in sameSample)
        {
            if (group.Key.Size <= WholeSampleLimit)
            {
                continue;
            }

            var files = group.ToList();
            toRead += files.Count;
            if (group.Key.Size > ChunkLength && files.Count <= MaxSideBySide)
            {
                sideBySide.Add(files);
            }
            else
            {
                oneByOne.AddRange(files);
            }
        }

        int done = 0;
        reporter.Report(3, 0, toRead, force: true);
        var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) };
        try
        {
            // The biggest sets first, so the long reads start early and the short ones fill in around them.
            Parallel.ForEach(sideBySide.OrderByDescending(files => files[0].Size * files.Count), options, files =>
                ReadSideBySide(files, hashes, reporter, () => reporter.Report(3, Interlocked.Increment(ref done), toRead, force: false), token));
            Parallel.ForEach(oneByOne, options, file =>
            {
                hashes[file] = HashWhole(file.FullPath, file.Size, out long read, token);
                reporter.Add(read);
                reporter.Report(3, Interlocked.Increment(ref done), toRead, force: false);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            throw new OperationCanceledException(token);
        }
        reporter.Report(3, toRead, toRead, force: true);

        var groups = new List<DuplicateGroup>();
        foreach (IGrouping<(long Size, string Sample), FsNode> group in sameSample)
        {
            IEnumerable<IGrouping<string, FsNode>> byHash = group
                .Select(file => (File: file, Hash: file.Size > WholeSampleLimit ? hashes.GetValueOrDefault(file) : group.Key.Sample))
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

    /// <summary>One file of a set being read side by side.</summary>
    /// <param name="File">The node.</param>
    /// <param name="Handle">Its open handle.</param>
    /// <param name="Hash">The SHA-256 of what has been read of it so far.</param>
    private sealed record Reading(FsNode File, SafeFileHandle Handle, IncrementalHash Hash);

    /// <summary>Reads the files of one same-size, same-sample set a chunk at a time, all of them up to the same point, and drops a file
    /// as soon as its contents so far match no other file's. Only files with a twin are read to the end.</summary>
    /// <param name="files">The set; every file has the same recorded size, over <see cref="ChunkLength"/>.</param>
    /// <param name="hashes">Receives each file's whole-file SHA-256 as lowercase hex, or null for a file that was dropped or could not be read.</param>
    /// <param name="reporter">Counts the bytes read.</param>
    /// <param name="fileDone">Called once per file, when it is dropped or read to the end.</param>
    /// <param name="token">Stops the read between chunks.</param>
    private static void ReadSideBySide(List<FsNode> files, ConcurrentDictionary<FsNode, string?> hashes, Reporter reporter, Action fileDone, CancellationToken token)
    {
        long size = files[0].Size;
        var live = new List<Reading>(files.Count);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkLength);
        try
        {
            foreach (FsNode file in files)
            {
                hashes[file] = null;
                try
                {
                    SafeFileHandle handle = Open(file.FullPath);
                    if (RandomAccess.GetLength(handle) != size)
                    {
                        handle.Dispose();   // changed since the scan
                        fileDone();
                        continue;
                    }
                    live.Add(new Reading(file, handle, IncrementalHash.CreateHash(HashAlgorithmName.SHA256)));
                }
                catch (Exception ex) when (ex.IsFileSystemFailure() || ex is NotSupportedException or ArgumentException)
                {
                    fileDone();
                }
            }

            long offset = 0;
            while (offset < size && live.Count > 1)
            {
                token.ThrowIfCancellationRequested();
                int want = (int)Math.Min(ChunkLength, size - offset);
                var bySoFar = new Dictionary<string, List<Reading>>(StringComparer.Ordinal);
                foreach (Reading reading in live)
                {
                    int got;
                    try
                    {
                        got = ReadAt(reading.Handle, buffer.AsSpan(0, want), offset);
                    }
                    catch (Exception ex) when (ex.IsFileSystemFailure() || ex is NotSupportedException)
                    {
                        got = -1;
                    }
                    reporter.Add(Math.Max(0, got));
                    if (got != want)
                    {
                        Close(reading);   // unreadable or shorter than recorded
                        fileDone();
                        continue;
                    }

                    reading.Hash.AppendData(buffer, 0, want);
                    string soFar = Convert.ToHexString(reading.Hash.GetCurrentHash());
                    if (!bySoFar.TryGetValue(soFar, out List<Reading>? alike))
                    {
                        alike = new List<Reading>();
                        bySoFar[soFar] = alike;
                    }
                    alike.Add(reading);
                }

                // A file alone with its contents so far has no copy in the set; reading on would only confirm that.
                live.Clear();
                foreach (List<Reading> alike in bySoFar.Values)
                {
                    if (alike.Count > 1)
                    {
                        live.AddRange(alike);
                    }
                    else
                    {
                        Close(alike[0]);
                        fileDone();
                    }
                }
                offset += want;
            }

            if (offset >= size)
            {
                foreach (Reading reading in live)
                {
                    hashes[reading.File] = Convert.ToHexString(reading.Hash.GetHashAndReset()).ToLowerInvariant();
                }
            }
            foreach (Reading reading in live)
            {
                fileDone();
            }
        }
        finally
        {
            foreach (Reading reading in live)
            {
                Close(reading);
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Closes a file being read side by side.</summary>
    /// <param name="reading">The file; closing twice is harmless.</param>
    private static void Close(Reading reading)
    {
        reading.Handle.Dispose();
        reading.Hash.Dispose();
    }

    /// <summary>Reads into a buffer from a position until the buffer is full or the file ends.</summary>
    /// <param name="handle">The open file.</param>
    /// <param name="buffer">Where the bytes go; its length is how many are wanted.</param>
    /// <param name="offset">The position in the file.</param>
    /// <returns>How many bytes were read; less than the buffer's length only at the end of the file.</returns>
    private static int ReadAt(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        int total = 0;
        int read;
        while (total < buffer.Length && (read = RandomAccess.Read(handle, buffer[total..], offset + total)) > 0)
        {
            total += read;
        }
        return total;
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

    /// <summary>The SHA-256 of a file's samples: its first, middle and last 16 KB, or the whole file when it is no longer than the three
    /// together.</summary>
    /// <param name="path">The file.</param>
    /// <param name="expectedSize">The size the scan recorded; a file of another length now is treated as unreadable.</param>
    /// <param name="bytesRead">How many bytes were read; 0 when the file could not be opened.</param>
    /// <returns>The hash as lowercase hex, or null when the file could not be read or has changed size.</returns>
    private static string? HashSample(string path, long expectedSize, out long bytesRead)
    {
        bytesRead = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(WholeSampleLimit);
        try
        {
            using SafeFileHandle handle = Open(path);
            long length = RandomAccess.GetLength(handle);
            if (length != expectedSize)
            {
                return null;
            }

            int total;
            if (length <= WholeSampleLimit)
            {
                total = ReadAt(handle, buffer.AsSpan(0, (int)length), 0);
            }
            else
            {
                total = ReadAt(handle, buffer.AsSpan(0, SampleLength), 0);
                total += ReadAt(handle, buffer.AsSpan(total, SampleLength), (length - SampleLength) / 2);
                total += ReadAt(handle, buffer.AsSpan(total, SampleLength), length - SampleLength);
            }
            bytesRead = total;
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, total))).ToLowerInvariant();
        }
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is NotSupportedException or ArgumentException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The SHA-256 of a whole file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="expectedSize">The size the scan recorded; a file of another length now is treated as unreadable.</param>
    /// <param name="bytesRead">How many bytes this call read, also when it gave up part way.</param>
    /// <param name="token">Stops the read between buffers.</param>
    /// <returns>The hash as lowercase hex, or null when the file could not be read or has changed size.</returns>
    private static string? HashWhole(string path, long expectedSize, out long bytesRead, CancellationToken token)
    {
        bytesRead = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferLength);
        try
        {
            using SafeFileHandle handle = Open(path);
            if (RandomAccess.GetLength(handle) != expectedSize)
            {
                return null;
            }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = RandomAccess.Read(handle, buffer.AsSpan(0, BufferLength), bytesRead)) > 0)
            {
                token.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                bytesRead += read;
            }
            return bytesRead == expectedSize ? Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() : null;
        }
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is NotSupportedException or ArgumentException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
