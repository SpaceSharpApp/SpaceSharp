using Avalonia.Controls;

namespace SpaceSharp.Util;

/// <summary>Shows a dialog and dims the main window behind it while open.</summary>
public static class Modal
{
    /// <summary>Shows the dialog over its owner and waits until it closes.</summary>
    /// <param name="dialog">The window to show; it is closed by its own buttons, not by this call.</param>
    /// <param name="owner">The window to block; only a <see cref="MainWindow"/> owner is dimmed, any other owner just shows the dialog.</param>
    /// <returns>Completes on the UI thread once the dialog has closed and the owner is undimmed.</returns>
    public static async Task ShowModal(this Window dialog, Window owner)
    {
        // Only the main window has a dimmer; dialogs over other windows just show.
        var mainWindow = owner as MainWindow;
        mainWindow?.Dim(true);
        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            mainWindow?.Dim(false);
        }
    }

    /// <summary>Shows the dialog over its owner and waits for its result.</summary>
    /// <typeparam name="T">The type the dialog passes to <c>Close(result)</c>.</typeparam>
    /// <param name="dialog">The window to show; it is closed by its own buttons, not by this call.</param>
    /// <param name="owner">The window to block; only a <see cref="MainWindow"/> owner is dimmed, any other owner just shows the dialog.</param>
    /// <returns>The value the dialog closed with, or default when it closed without one (the title bar X, Escape).</returns>
    public static async Task<T?> ShowModal<T>(this Window dialog, Window owner)
    {
        var mainWindow = owner as MainWindow;
        mainWindow?.Dim(true);
        try
        {
            return await dialog.ShowDialog<T?>(owner);
        }
        finally
        {
            mainWindow?.Dim(false);
        }
    }
}
