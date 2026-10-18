using Avalonia.Input;
using Avalonia.Input.Platform;

namespace SpaceSharp.Util;

/// <summary>Clipboard text helpers over the Avalonia DataTransfer API.</summary>
internal static class ClipboardText
{
    public static Task SetAsync(IClipboard clipboard, string text)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Text, text));
        return clipboard.SetDataAsync(transfer);
    }

    /// <summary>The clipboard's text, or null.</summary>
    public static async Task<string?> GetAsync(IClipboard clipboard)
    {
        var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null) return null;
        return await transfer.TryGetTextAsync();
    }
}
