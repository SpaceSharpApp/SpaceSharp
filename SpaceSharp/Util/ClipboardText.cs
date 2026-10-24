using Avalonia.Input;
using Avalonia.Input.Platform;

namespace SpaceSharp.Util;

/// <summary>Clipboard text helpers over the Avalonia DataTransfer API.</summary>
internal static class ClipboardText
{
    /// <summary>Replaces the clipboard contents with plain text.</summary>
    /// <param name="clipboard">The window's clipboard, from <c>TopLevel.Clipboard</c>; must not be null.</param>
    /// <param name="text">The text to place; an empty string clears the text content.</param>
    /// <returns>Completes when the platform has accepted the data.</returns>
    public static Task SetAsync(IClipboard clipboard, string text)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Text, text));
        return clipboard.SetDataAsync(transfer);
    }

    /// <summary>The clipboard's text, or null.</summary>
    /// <param name="clipboard">The window's clipboard, from <c>TopLevel.Clipboard</c>; must not be null.</param>
    /// <returns>The text content, or null when the clipboard is empty or holds no text format.</returns>
    public static async Task<string?> GetAsync(IClipboard clipboard)
    {
        IAsyncDataTransfer? transfer = await clipboard.TryGetDataAsync();
        if (transfer is null)
        {
            return null;
        }

        return await transfer.TryGetTextAsync();
    }
}
