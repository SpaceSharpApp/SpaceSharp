using Velopack;
using Velopack.Sources;

namespace SpaceSharp.Services;

/// <summary>Checks GitHub Releases for a newer version and installs it via Velopack.</summary>
/// <remarks>Inactive for portable copies not installed through a Velopack setup.</remarks>
internal sealed class Updater
{
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";

    private readonly UpdateManager _manager = new(new GithubSource(Repository, null, prerelease: false));

    /// <summary>The single updater; the manager keeps the state of the last check.</summary>
    public static Updater Instance { get; } = new();

    /// <summary>True when this copy was installed by a Velopack setup and can update itself; false for a portable exe.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The installed version as Velopack sees it, or "unknown" for a portable copy.</summary>
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "unknown";

    /// <summary>The update found by the last check; null before any check, when up to date, or when the check failed.</summary>
    public UpdateInfo? Available
    {
        get; private set;
    }

    /// <summary>The version of <see cref="Available"/> as text, or null when there is none.</summary>
    public string? AvailableVersion => Available?.TargetFullRelease.Version.ToString();

    /// <summary>Asks GitHub Releases for a newer version and remembers the answer in <see cref="Available"/>.</summary>
    /// <returns>The available update, or null if up to date, not installed, or offline.</returns>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsInstalled)
        {
            return null;
        }

        try
        {
            Available = await _manager.CheckForUpdatesAsync();
        }
        catch (Exception)
        {
            // Offline, rate limited or a GitHub hiccup: a failed check is the same as no update.
            Available = null;
        }

        return Available;
    }

    /// <summary>Downloads the update, then exits and restarts into the new version.</summary>
    /// <remarks>Applies silently; Velopack's own progress window cannot be themed. Does nothing when no update is <see cref="Available"/>.</remarks>
    /// <param name="progress">Receives the download percentage, 0 to 100, on a thread pool thread; null for no reporting.</param>
    /// <returns>Does not complete normally: the process exits once the download is done.</returns>
    public async Task InstallAndRestartAsync(Action<int>? progress = null)
    {
        if (Available is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(Available, progress);
        _manager.WaitExitThenApplyUpdates(Available, silent: true, restart: true);
        Environment.Exit(0);
    }
}
