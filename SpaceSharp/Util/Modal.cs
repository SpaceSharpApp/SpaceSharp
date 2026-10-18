using Avalonia.Controls;

namespace SpaceSharp.Util;

/// <summary>Shows a dialog and dims the main window behind it while open.</summary>
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
