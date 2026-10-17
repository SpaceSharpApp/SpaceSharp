using Avalonia.Controls;

namespace SpaceSharp.Util;

/// <summary>
/// Shows a window over another and, when the owner is the main window, dims the map behind it for as long as it is
/// open (Settings › Appearance › Dim behind windows sets how much). Every window the app opens over the map goes
/// through here instead of ShowDialog, so the dim never gets out of step with what is open.
/// </summary>
public static class Modal
{
    public static async Task ShowModal(this Window dialog, Window owner)
    {
        var main = owner as MainWindow;
        main?.Dim(true);
        try { await dialog.ShowDialog(owner); }
        finally { main?.Dim(false); }
    }

    public static async Task<T?> ShowModal<T>(this Window dialog, Window owner)
    {
        var main = owner as MainWindow;
        main?.Dim(true);
        try { return await dialog.ShowDialog<T?>(owner); }
        finally { main?.Dim(false); }
    }
}
