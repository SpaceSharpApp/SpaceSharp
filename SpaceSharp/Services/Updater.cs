using Velopack;
using Velopack.Sources;

namespace SpaceSharp.Services;

/// <summary>Checks GitHub Releases for a newer version and installs it via Velopack.</summary>
/// <remarks>Inactive for portable copies not installed through a Velopack setup.</remarks>
internal sealed class Updater
{
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";

    private readonly UpdateManager _manager = new(new GithubSource(Repository, null, prerelease: false));

    public static Updater Instance { get; } = new();

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "unknown";

    /// <summary>The update found by the last check.</summary>
    public UpdateInfo? Available { get; private set; }

    public string? AvailableVersion => Available?.TargetFullRelease.Version.ToString();

    /// <summary>Returns the available update, or null if up to date, not installed, or offline.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsInstalled) return null;
        try
        {
            Available = await _manager.CheckForUpdatesAsync();
        }
        catch (Exception)
        {
            Available = null; // offline or rate limited
        }
        return Available;
    }

    /// <summary>Downloads the update, then exits and restarts into the new version.</summary>
    /// <remarks>Applies silently; Velopack's own progress window cannot be themed.</remarks>
    public async Task InstallAndRestartAsync(Action<int>? progress = null)
    {
        if (Available is null) return;
        await _manager.DownloadUpdatesAsync(Available, progress);
        _manager.WaitExitThenApplyUpdates(Available, silent: true, restart: true);
        Environment.Exit(0);
    }
}
