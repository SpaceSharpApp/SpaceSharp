using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SpaceSharp.Util;

/// <summary>The Save dialog, asked the same way everywhere a file is written.</summary>
public static class SaveDialog
{
    /// <summary>Asks where to save one file of a single type.</summary>
    /// <param name="owner">The window the dialog belongs to.</param>
    /// <param name="title">The dialog's caption.</param>
    /// <param name="suggestedName">The file name offered, extension included.</param>
    /// <param name="typeName">What the file type is called in the dialog's type list, such as "PNG" or "SpaceSharp scan".</param>
    /// <param name="extension">The extension without its dot; it is the only pattern offered and is added when the name has none.</param>
    /// <param name="startFolder">The folder the dialog opens in; null leaves it where the user was last.</param>
    /// <param name="mimeType">The type's MIME name for platforms that pick by it; null when there is none worth giving.</param>
    /// <returns>The chosen path; null when the dialog was cancelled or the place picked is not a local path.</returns>
    public static async Task<string?> PickSavePathAsync(
        this TopLevel owner,
        string title,
        string suggestedName,
        string typeName,
        string extension,
        string? startFolder = null,
        string? mimeType = null)
    {
        IStorageProvider storage = owner.StorageProvider;
        var type = new FilePickerFileType(typeName)
        {
            Patterns = new[] { "*." + extension },
            MimeTypes = mimeType is null ? null : new[] { mimeType }
        };
        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = new[] { type },
            SuggestedStartLocation = startFolder is null ? null : await storage.TryGetFolderFromPathAsync(startFolder)
        });
        return file?.TryGetLocalPath();
    }
}
