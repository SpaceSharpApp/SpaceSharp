using Avalonia.Threading;

namespace SpaceSharp.Util;

/// <summary>Helpers for tasks nobody awaits.</summary>
public static class BackgroundTasks
{
    /// <summary>
    /// Lets a task run on its own and reports its failure the moment it fails. A task dropped with
    /// <c>_ =</c> keeps its exception to itself until the garbage collector finds it, which may be minutes
    /// later or never; this sends it to the crash report at once.
    /// </summary>
    /// <param name="task">The started task; cancellation is not a failure and is ignored.</param>
    public static void Detach(this Task task) =>
        _ = task.ContinueWith(
            failed =>
            {
                if (failed.Exception is { } failure)
                {
                    Dispatcher.UIThread.Post(() => App.Report(failure.GetBaseException(), "background task"));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
