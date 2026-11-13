using System.ComponentModel;
using System.Text.Json;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>The named exception sets: what counts as the file system or the shell saying no.</summary>
public class FailuresTests
{
    [Fact]
    public void FileSystemFailureCoversIoAndAccess()
    {
        Assert.True(new IOException().IsFileSystemFailure());
        Assert.True(new FileNotFoundException().IsFileSystemFailure());
        Assert.True(new DirectoryNotFoundException().IsFileSystemFailure());
        Assert.True(new PathTooLongException().IsFileSystemFailure());
        Assert.True(new EndOfStreamException().IsFileSystemFailure());
        Assert.True(new UnauthorizedAccessException().IsFileSystemFailure());
    }

    [Fact]
    public void FileSystemFailureLeavesBugsAlone()
    {
        Assert.False(new InvalidOperationException().IsFileSystemFailure());
        Assert.False(new ArgumentException().IsFileSystemFailure());
        Assert.False(new FormatException().IsFileSystemFailure());
        Assert.False(new JsonException().IsFileSystemFailure());
        Assert.False(new Win32Exception().IsFileSystemFailure());
    }

    [Fact]
    public void LaunchFailureCoversTheShellAndIo()
    {
        Assert.True(new Win32Exception().IsLaunchFailure());
        Assert.True(new IOException().IsLaunchFailure());
        Assert.False(new UnauthorizedAccessException().IsLaunchFailure());
        Assert.False(new InvalidOperationException().IsLaunchFailure());
    }
}
