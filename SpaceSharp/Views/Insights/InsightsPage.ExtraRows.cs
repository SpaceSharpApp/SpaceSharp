using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Insights view: the rows under the first three: largest items, duplicates, empty folders and the rest.</summary>
public sealed partial class InsightsPage
{
    /// <summary>The Games row: one line per store with its libraries and game count; left out when no store was found.</summary>
    private void BuildGamesRow()
    {
        IReadOnlyList<GameStore> stores = Extra.Games;
        if (stores.Count == 0)
        {
            return;
        }
        long total = stores.Sum(store => store.Bytes);
        IBrush[] fills = { TileBrush("GoodBar"), Shade(TileBrush("GoodBar"), 0.8), Shade(TileBrush("GoodBar"), 0.6), Shade(TileBrush("GoodBar"), 0.45) };
        Grid visual = StackedBar(stores.Take(4).Select((store, i) => ((double)store.Bytes / Math.Max(1, total), fills[i])));
        string facts = Facts(stores.Take(3).Select(store => StoreFact(store)).ToArray());
        Row("Games", Strings.Get("Insights_RowGames"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        foreach (GameStore store in stores)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80"), Margin = new Thickness(0, 10, 0, 0) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = StoreFact(store), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold });
            foreach (FsNode folder in store.Folders)
            {
                TextBlock path = Small(folder.FullPath);
                FsNode capturedFolder = folder;
                Clickable(path, () => Host.ShowOnMap(capturedFolder));
                text.Children.Add(path);
            }
            line.Children.Add(text);
            var size = new TextBlock { Text = SizeFormatter.Format(store.Bytes), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            body.Children.Add(line);
        }
    }

    /// <summary>"Steam 131 GB in 23 games", localized.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The sentence; "in 1 game" for one.</returns>
    private static string StoreFact(GameStore store) => store.Games == 1
        ? Strings.Format("Insights_StoreGamesOne", store.Store, SizeFormatter.Format(store.Bytes))
        : Strings.Format("Insights_StoreGames", store.Store, SizeFormatter.Format(store.Bytes), store.Games);

    /// <summary>The Media row: images, audio and video, and the screenshot folders; left out when the scan holds none.</summary>
    private void BuildMediaRow()
    {
        MediaFacts media = Extra.Media;
        long total = media.Images + media.Audio + media.Video;
        if (total == 0)
        {
            return;
        }
        ColorScheme scheme = Host.Scheme;
        IBrush Fill(FileCategory category) => scheme.Categories.First(categoryColor => categoryColor.Category == category).Brush;
        Grid visual = StackedBar(new[]
        {
            ((double)media.Images / total, Fill(FileCategory.Images)),
            ((double)media.Audio / total, Fill(FileCategory.Audio)),
            ((double)media.Video / total, Fill(FileCategory.Video))
        });
        string facts = Facts(
            Strings.Format("Insights_FactImages", SizeFormatter.Format(media.Images), media.ImageFiles),
            Strings.Format("Insights_FactAudio", SizeFormatter.Format(media.Audio)),
            Strings.Format("Insights_FactVideo", SizeFormatter.Format(media.Video)),
            media.ScreenshotFiles == 0 ? null : Strings.Format("Insights_FactScreenshots", media.ScreenshotFiles, SizeFormatter.Format(media.Screenshots)));
        Row("Media", Strings.Get("Insights_RowMedia"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        void AddLine(FileCategory category, long bytes, int files, string filter)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("14,120,*,90,80"), ColumnSpacing = 10 };
            line.Children.Add(Swatch(Fill(category)));
            var name = new TextBlock { Text = Strings.Category(category), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            line.Children.Add(name);
            Border bar = Bar((double)bytes / total, Fill(category), 5);
            Grid.SetColumn(bar, 2);
            line.Children.Add(bar);
            TextBlock count = Small(Strings.Format("Panel_TypeFiles", files));
            count.TextAlignment = TextAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 3);
            line.Children.Add(count);
            var size = new TextBlock { Text = SizeFormatter.Format(bytes), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 4);
            line.Children.Add(size);
            Border row = Hairline(line, 30);
            Clickable(row, () => Host.FilterOnMap(filter));
            body.Children.Add(row);
        }
        AddLine(FileCategory.Images, media.Images, media.ImageFiles, "type:images");
        AddLine(FileCategory.Audio, media.Audio, media.AudioFiles, "type:audio");
        AddLine(FileCategory.Video, media.Video, media.VideoFiles, "type:video");
        if (media.ScreenshotFiles > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_ScreenshotsNote", media.ScreenshotFiles, SizeFormatter.Format(media.Screenshots)), FontSize = FontSizes.Small, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        }
    }

    /// <summary>The Developer row: node_modules, .git and build output; left out when none were found.</summary>
    private void BuildDeveloperRow()
    {
        if (Extra.Developer is not { } dev)
        {
            return;
        }
        long total = dev.NodeModules + dev.Git + dev.BuildOutput;
        IBrush code = Host.Scheme.Categories.First(categoryColor => categoryColor.Category == FileCategory.Code).Brush;
        Grid visual = StackedBar(new[]
        {
            ((double)dev.NodeModules / Math.Max(1, total), code),
            ((double)dev.Git / Math.Max(1, total), Shade(code, 0.7)),
            ((double)dev.BuildOutput / Math.Max(1, total), this.ThemeBrush("TextDim", Brushes.Gray))
        });
        string facts = Facts(
            dev.Projects == 0 ? null : Strings.Format("Insights_FactNodeModules", SizeFormatter.Format(dev.NodeModules), dev.Projects),
            dev.Repos == 0 ? null : Strings.Format("Insights_FactGit", SizeFormatter.Format(dev.Git), dev.Repos),
            dev.BuildFolders == 0 ? null : Strings.Format("Insights_FactBuild", SizeFormatter.Format(dev.BuildOutput), dev.BuildFolders),
            Strings.Get("Insights_AllRebuildable"));
        Row("Developer", Strings.Get("Insights_RowDeveloper"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DeveloperExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        AddFolderLines(body, dev.Largest, Host.Measure);
    }

    /// <summary>The Downloads row: what the folder holds, installers already run and archives already extracted; left out when the folder is not in the scan.</summary>
    private void BuildDownloadsRow()
    {
        if (Extra.Downloads is not { } downloads)
        {
            return;
        }
        SizeMeasure measure = Host.Measure;
        IBrush accent = this.ThemeBrush("Accent", Palette.NewBrush);
        var byMonth = DriveInsights.ByMonth(downloads.Folder, measure, _insightsNow, 12).Select(bucket => bucket.Bytes).ToList();
        Grid visual = Sparkline(byMonth, Enumerable.Repeat(accent, byMonth.Count).ToList());
        string facts = Facts(
            downloads.OldestUtc is { } oldest ? Strings.Format("Insights_FactOldest", oldest.ToLocalTime().ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture)) : null,
            downloads.Installers == 0 ? null : Strings.Format("Insights_FactInstallers", downloads.Installers, SizeFormatter.Format(downloads.InstallerBytes)),
            downloads.ExtractedArchives == 0 ? null : Strings.Format("Insights_FactExtracted", downloads.ExtractedArchives, SizeFormatter.Format(downloads.ExtractedBytes)));
        Row("Downloads", Strings.Get("Insights_RowDownloads"), SizeFormatter.Format(downloads.Bytes), visual, facts, out StackPanel body);

        TextBlock path = Small(downloads.Folder.FullPath);
        path.Margin = new Thickness(0, 8, 0, 6);
        Clickable(path, () => Host.ShowOnMap(downloads.Folder));
        body.Children.Add(path);
        if (downloads.Installers > 0 || downloads.ExtractedArchives > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DownloadsExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        }
        AddFileLines(body, downloads.Largest, measure);
    }

    /// <summary>The Hard links row: what counting them once saved; left out when the scan has no duplicates.</summary>
    private void BuildHardLinksRow()
    {
        if (Extra.HardLinks is not { } links)
        {
            return;
        }
        string facts = Facts(
            links.TopFolder is null
                ? Strings.Format("Insights_FactLinks", links.LinkCount)
                : Strings.Format("Insights_FactLinksMostly", links.LinkCount, PathWithin(links.TopFolder)),
            Strings.Format("Insights_FactLinksFuller", SizeFormatter.Format(links.BytesSaved)));
        Row("HardLinks", Strings.Get("Insights_RowHardLinks"), SizeFormatter.Format(links.BytesSaved), VisualText(Strings.Get("Insights_CountedOnce")), facts, out StackPanel body);
        body.Children.Add(new TextBlock { Text = Strings.Get("Insights_HardLinksExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        if (links.TopFolder is { } topFolder)
        {
            AddFolderLines(body, new[] { topFolder }, Host.Measure, links.TopFolderBytes);
        }
    }

    /// <summary>The Small and empty row: cluster slack, tiny files and empty folders.</summary>
    private void BuildSmallAndEmptyRow()
    {
        SmallAndEmptyFacts small = Extra.SmallAndEmpty;
        string facts = Facts(
            Strings.Format("Insights_FactTiny", small.TinyFiles),
            Strings.Format("Insights_FactZeroByte", small.ZeroByteFiles),
            Strings.Format("Insights_FactEmpty", small.EmptyFolders),
            Strings.Format("Insights_FactSingle", small.SingleFileFolders));
        Button? review = null;
        if (small.EmptyFolders > 0)
        {
            review = new Button { Content = Strings.Get("Insights_ReviewEmpty"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
            review.Click += async (_, _) => await Host.ReviewEmptyFoldersAsync(Root!);
        }
        Row("SmallEmpty", Strings.Get("Insights_RowSmall"), SizeFormatter.Format(small.SlackBytes), VisualText(Strings.Get("Insights_OfSlack")), facts, out StackPanel body, review);
        body.Children.Add(new TextBlock
        {
            Text = Strings.Format("Insights_SmallExplain", small.TinyFiles, SizeFormatter.Format(small.TinyBytes), SizeFormatter.Format(small.SlackBytes), small.EmptyFolders),
            FontSize = FontSizes.Small,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17,
            Classes = { "dim" }
        });

        // What the row counts, each with a way to see it on the map.
        FsNode root = Root!;
        var lines = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        lines.Children.Add(FactLine(Strings.Get("Stats_Slack"), Strings.Get("Stats_SlackDetail"), SizeFormatter.Format(small.SlackBytes), null));
        lines.Children.Add(FactLine(Strings.Get("Stats_Tiny"), SizeFormatter.Format(small.TinyBytes), small.TinyFiles.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            small.TinyFiles > 0 ? () => Host.FilterInFolder(root, $"is:file <{StatisticsReport.TinyLimit - 1}B") : null));
        lines.Children.Add(FactLine(Strings.Get("Stats_ZeroByte"), string.Empty, small.ZeroByteFiles.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            small.ZeroByteFiles > 0 ? () => Host.FilterInFolder(root, "is:file <0B") : null));
        lines.Children.Add(FactLine(Strings.Get("Stats_EmptyFolders"), string.Empty, small.EmptyFolders.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            small.EmptyFolders > 0 ? () => _ = Host.ReviewEmptyFoldersAsync(root) : null));
        body.Children.Add(lines);
    }

    /// <summary>The Duplicates row: a button to search until a search ran, then the sets found and what deleting the extra copies would free.</summary>
    private void BuildDuplicatesRow()
    {
        DuplicateResult? found = Host.Duplicates;
        var findButton = new Button { Content = Strings.Get("Insights_FindDuplicates"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
        findButton.Click += async (_, _) => await Host.OpenDuplicatesAsync();
        if (found is null)
        {
            Row("Duplicates", Strings.Get("Insights_RowDuplicates"), Strings.Get("Insights_DupNotSearched"), null, Strings.Get("Insights_DupFacts"), out StackPanel emptyBody, findButton, dim: true);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_DupExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return;
        }

        long wasted = found.Groups.Sum(group => group.Wasted);
        int copies = found.Groups.Sum(group => group.Files.Count - 1);
        string facts = Facts(
            Strings.Format("Insights_FactDupSets", found.Groups.Count),
            Strings.Format("Insights_FactDupCopies", copies),
            Strings.Format("Insights_FactDupRead", SizeFormatter.Format(found.BytesRead)));
        Row("Duplicates", Strings.Get("Insights_RowDuplicates"), SizeFormatter.Format(wasted), VisualText(Strings.Get("Insights_DupCouldGo")), facts, out StackPanel body, findButton);
        if (found.Groups.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DupNoneRow"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
            return;
        }
        body.Children.Add(new Border { Height = 6 });
        foreach (DuplicateGroup group in found.Groups.Take(6))
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var size = new TextBlock { Text = SizeFormatter.Format(group.Wasted), FontSize = FontSizes.Small, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(size, Dock.Right);
            line.Children.Add(size);
            var text = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            text.Text = group.Files[0].Name + "  " + Strings.Format("Insights_DupCopiesOf", group.Files.Count);
            ToolTip.SetTip(text, string.Join("\n", group.Files.Select(file => file.FullPath)));
            line.Children.Add(text);
            body.Children.Add(line);
        }
    }

    /// <summary>The Deep and long row: the deepest folder and the paths over 260 characters, with a loop called out and a way to delete its repeats.</summary>
    private void BuildDeepAndLongRow()
    {
        DeepAndLongFacts deep = Extra.DeepAndLong;
        string facts = Facts(
            deep.Deepest is null ? null : PathShort(deep.Deepest),
            deep.Longest.Count == 0 ? null : PathShort(deep.Longest[0]));
        Row("DeepLong", Strings.Get("Insights_RowDeep"), Strings.Format("Insights_DeepNumber", deep.MaxDepth), VisualText(Strings.Format("Insights_LongPaths", deep.LongPaths)), facts, out StackPanel body);

        if (deep.Deepest is { } deepest)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_DeepestIs", deep.MaxDepth), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 2), Classes = { "dim" } });
            body.Children.Add(PathLine(deepest));

            // A folder that nests a copy of itself over and over is a loop; the fix is to delete the copies below the first.
            if (PathLoops.Find(deepest.FullPath) is { } loop && LoopRepeatRoot(deepest, loop) is { } repeatRoot)
            {
                var note = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var deleteButton = new Button { Content = Strings.Get("Insights_DeleteRepeats"), Classes = { "danger-outline" }, Height = 28, FontSize = FontSizes.Small, Padding = new Thickness(10, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                FsNode captured = repeatRoot;
                deleteButton.Click += (_, _) => _ = Host.DeletePermanentlyAsync(new[] { captured });
                ToolTip.SetTip(deleteButton, Strings.Format("Insights_DeleteRepeatsTip", loop.Repeats - 1, PathLoops.Compact(repeatRoot.FullPath, 160)));
                DockPanel.SetDock(deleteButton, Dock.Right);
                note.Children.Add(deleteButton);
                note.Children.Add(new TextBlock
                {
                    Text = Strings.Format("Insights_PathRepeats", loop.Unit, loop.Repeats),
                    FontSize = FontSizes.Small,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Classes = { "dim" }
                });
                body.Children.Add(note);
            }
        }
        if (deep.Longest.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_LongestAre", deep.LongPaths), FontSize = FontSizes.Small, Margin = new Thickness(0, 12, 0, 2), Classes = { "dim" } });
            foreach (FsNode node in deep.Longest)
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
                line.Children.Add(PathLine(node));
                TextBlock length = Small(Strings.Format("Insights_Chars", node.FullPath.Length));
                length.TextAlignment = TextAlignment.Right;
                length.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(length, 1);
                line.Children.Add(length);
                body.Children.Add(line);
            }
        }
    }

    /// <summary>A path on one line, shortened from the middle when long, with the whole path in the tooltip; clicking shows it on the map.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text block.</returns>
    private TextBlock PathLine(FsNode node)
    {
        var path = new TextBlock { Text = PathLoops.Compact(node.FullPath, 140), FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        ToolTip.SetTip(path, node.FullPath);
        FsNode captured = node;
        Clickable(path, () => Host.ShowOnMap(captured));
        return path;
    }

    /// <summary>The folder where a loop's second copy starts: deleting it keeps the first copy and removes every nested one.</summary>
    /// <param name="deepest">The folder at the bottom of the loop.</param>
    /// <param name="loop">The loop found in its path.</param>
    /// <returns>The folder at segment index Start + Period, or null when the tree does not reach it.</returns>
    private static FsNode? LoopRepeatRoot(FsNode deepest, PathLoop loop)
    {
        int depthOfDeepest = PathLoops.Segments(deepest.FullPath).Length - 1;
        int target = loop.Start + loop.Period;
        FsNode? node = deepest;
        for (int depth = depthOfDeepest; depth > target && node is not null; depth--)
        {
            node = node.Parent;
        }
        return node is { IsDirectory: true, Parent: not null } ? node : null;
    }
}
