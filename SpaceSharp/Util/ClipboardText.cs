using Avalonia.Input;
using Avalonia.Input.Platform;

namespace SpaceSharp.Util;

/// <summary>
/// Puts text on the clipboard and reads it back. Avalonia 12 dropped IClipboard.SetTextAsync for the DataTransfer API that 11.3
/// introduced; this is the one place that knows the new shape.
/// </summary>
internal static class ClipboardText
{
    public static Task SetAsync(IClipboard clipboard, string text)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Text, text));
        return clipboard.SetDataAsync(transfer);
    }

    /// <summary>The clipboard's text, or null when it holds none.</summary>
    public static async Task<string?> GetAsync(IClipboard clipboard)
    {
        var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null) return null;
        return await transfer.TryGetTextAsync();
    }
}
