namespace SpaceSharp.Util;

/// <summary>Names the failures the app expects from the outside world, so a catch says what it tolerates in one word and the rule lives in one place.</summary>
public static class Failures
{
    /// <summary>Tells whether the file system refused: a file or folder that is missing, locked, in use, too long to name, or not ours to touch.</summary>
    /// <param name="e">The exception a file, folder or drive operation threw.</param>
    /// <returns>True for <see cref="IOException"/> and everything derived from it, and for <see cref="UnauthorizedAccessException"/>; false for anything that points at a bug instead.</returns>
    public static bool IsFileSystemFailure(this Exception e) => e is IOException or UnauthorizedAccessException;

    /// <summary>Tells whether Windows could not start a program or open a file, folder or link: nothing is registered for it, the target is gone, or the shell refused.</summary>
    /// <param name="e">The exception a process start or shell open threw.</param>
    /// <returns>True for <see cref="System.ComponentModel.Win32Exception"/> and <see cref="IOException"/>.</returns>
    public static bool IsLaunchFailure(this Exception e) => e is System.ComponentModel.Win32Exception or IOException;
}
