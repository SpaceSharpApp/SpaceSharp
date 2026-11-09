using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The sample map beside the Treemap tab, built from a small pretend tree and refreshed with every change.</summary>
public partial class SettingsWindow
{
    private TreemapControl? _preview;
    private FsNode? _previewSelected;

    /// <summary>A small live treemap of the sample tree for the Treemap tab, with a caption saying it is sample data.</summary>
    /// <returns>The framed map with its caption; <see cref="RefreshPreview"/> keeps it in step with the settings.</returns>
    private Control PreviewMap()
    {
        _preview = new TreemapControl { Height = 168, IsHitTestVisible = false, Focusable = false, Root = SampleTree(out _previewSelected) };
        _preview.Bind(TreemapControl.MapBackgroundProperty, this.GetResourceObservable("MapBackground"));
        RefreshPreview();
        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true, Child = _preview, Classes = { "frame" } };
        var panel = new DockPanel();
        var caption = new TextBlock { Text = Strings.Get("Settings_PreviewSample"), FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Classes = { "dim" } };
        DockPanel.SetDock(caption, Dock.Bottom);
        panel.Children.Add(caption);
        panel.Children.Add(frame);
        return panel;
    }

    /// <summary>Re-applies the current map settings to the preview, if one is showing.</summary>
    private void RefreshPreview()
    {
        if (_preview is null)
        {
            return;
        }
        MainWindow.ApplyLook(_preview, _settings, App.IsDark);
        _preview.SelectedNode = _previewSelected;
    }

    /// <summary>A sample tree for previews; sizes in GB.</summary>
    /// <param name="selected">The file to show as selected in previews: the largest photo.</param>
    /// <returns>A finished root with Photos, Videos, Programs (with a Steam subfolder), Documents and Music, about 100 GB in all; folder names are localized.</returns>
    internal static FsNode SampleTree(out FsNode selected)
    {
        var root = new FsNode(Strings.Get("Settings_PreviewDrive"), NodeKind.Directory, null);

        FsNode Dir(FsNode parent, string name)
        {
            var directory = new FsNode(name, NodeKind.Directory, parent);
            parent.Children.Add(directory);
            return directory;
        }

        void File(FsNode parent, string name, double gigabytes)
        {
            long size = (long)(gigabytes * 1024 * 1024 * 1024);
            parent.Children.Add(new FsNode(name, NodeKind.File, parent) { Size = size, Allocated = size, FileCount = 1, LastWriteUtc = DateTime.UtcNow });
        }

        FsNode photos = Dir(root, Strings.Get("Sample_Photos"));
        foreach ((string name, double gigabytes) in new[] { ("DSC_0412.NEF", 11.0), ("DSC_0388.NEF", 8.0), ("Summer.jpg", 5.0), ("Family.jpg", 4.0), ("Beach.jpg", 3.0), ("Trip.jpg", 3.0) })
        {
            File(photos, name, gigabytes);
        }

        FsNode videos = Dir(root, Strings.Get("Sample_Videos"));
        foreach ((string name, double gigabytes) in new[] { ("Holiday 2024.mp4", 14.0), ("Birthday.mov", 8.0), ("Clip.mp4", 4.0) })
        {
            File(videos, name, gigabytes);
        }

        FsNode programs = Dir(root, Strings.Get("Sample_Programs"));
        FsNode steam = Dir(programs, "Steam");
        foreach ((string name, double gigabytes) in new[] { ("game.pak", 5.0), ("textures.bin", 4.0), ("audio.bank", 2.0), ("steam.exe", 1.0) })
        {
            File(steam, name, gigabytes);
        }
        File(programs, "setup.msi", 4.0);
        File(programs, "tools.zip", 2.0);

        FsNode docs = Dir(root, Strings.Get("Sample_Documents"));
        foreach ((string name, double gigabytes) in new[] { ("Thesis.pdf", 3.0), ("Budget.xlsx", 2.0), ("Notes.docx", 2.0), ("a.txt", 1.0), ("b.txt", 1.0), ("c.txt", 1.0), ("d.txt", 1.0), ("e.txt", 1.0) })
        {
            File(docs, name, gigabytes);
        }

        FsNode music = Dir(root, Strings.Get("Sample_Music"));
        foreach ((string name, double gigabytes) in new[] { ("Album.flac", 3.0), ("Live.flac", 2.0), ("Mix.mp3", 2.0), ("Demo.wav", 2.0), ("Intro.mp3", 1.0) })
        {
            File(music, name, gigabytes);
        }

        foreach (FsNode directory in new[] { steam, photos, videos, programs, docs, music })
        {
            directory.FinishDirectory();
        }
        root.FinishDirectory();
        selected = photos.Children[0];
        return root;
    }
}
