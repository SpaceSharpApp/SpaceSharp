using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Achievements: judging scans and comparisons, and the toast in the map's corner when one unlocks.</summary>
public partial class MainWindow
{
    private readonly Queue<Achievement> _toastQueue = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly HashSet<MainView> _viewsSeen = new();
    private DateTime _scanStartedLocal;

    /// <summary>The store the window judges into; the mock PC never judges, so it never unlocks anything.</summary>
    private static Achievements Store => Achievements.Current;

    /// <summary>Subscribes to unlocks and wires the toast's timer and click; called once from the constructor.</summary>
    private void HookAchievements()
    {
        Store.Unlocked += achievement => Dispatcher.UIThread.Post(() =>
        {
            if (achievement == Achievement.Collector)
            {
                _ = ShowCollectorAsync();
            }
            else
            {
                QueueToast(achievement);
            }
        });
        _toastTimer.Tick += (_, _) => DismissToast();
        AchievementToast.PointerPressed += (_, _) => DismissToast();
        ToastIcon.Glyph = Icons.Get("Trophy");
    }

    /// <summary>The localized name of an achievement.</summary>
    /// <param name="achievement">The one to name.</param>
    /// <returns>The string under the key "Ach_", the achievement's name and "_Name".</returns>
    internal static string AchievementName(Achievement achievement) => Strings.Get("Ach_" + achievement + "_Name");

    /// <summary>The localized one-line hint of an achievement: what earns it, in the app's dry tone.</summary>
    /// <param name="achievement">The one to describe.</param>
    /// <returns>The string under the key "Ach_", the achievement's name and "_Hint".</returns>
    internal static string AchievementHint(Achievement achievement) => Strings.Get("Ach_" + achievement + "_Hint");

    /// <summary>The last achievement gets a window instead of a toast: the Collector card over a closing line.</summary>
    private async Task ShowCollectorAsync()
    {
        DismissToast();
        await Dialog.Card(this, AchievementName(Achievement.Collector), AchievementCard(Achievement.Collector), Strings.Get("Ach_CollectorMessage"));
    }

    /// <summary>The text in the Mirror, Mirror badge: the scan's name equal to itself.</summary>
    /// <param name="scanName">The drive letter or folder name.</param>
    /// <returns>"C:=C:" for a short name, "=" alone for a long one.</returns>
    internal static string MirrorBadgeText(string scanName) => scanName.Length <= 3 ? scanName + "=" + scanName : "=";

    /// <summary>An achievement as a card: an amber banner, a badge, the name and the hint. The one the dialogs and the list open.</summary>
    /// <param name="achievement">The achievement to show.</param>
    /// <param name="badgeText">Text for the badge instead of the trophy, such as "C:=C:"; null draws the trophy.</param>
    /// <param name="unlocked">True shows the hint and the "unlocked" banner; false hides the hint and says the card is locked. Default true.</param>
    /// <returns>The card, rounded and clipped, for <see cref="Dialog.Card"/>.</returns>
    internal static Border AchievementCard(Achievement achievement, string? badgeText = null, bool unlocked = true)
    {
        bool collector = achievement == Achievement.Collector;
        string kickerKey = !unlocked ? "Ach_Locked" : collector ? "Ach_AllUnlocked" : "Dialog_AchievementUnlocked";
        var kicker = new TextBlock
        {
            Text = Strings.Get(kickerKey).ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.2,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var bannerBox = new Border { Child = kicker, Padding = new Thickness(18, 12) };
        BindBrush(bannerBox, Border.BackgroundProperty, unlocked ? "Accent" : "Control");
        BindBrush(bannerBox, TextElement.ForegroundProperty, unlocked ? "AccentText" : "TextDim");

        double badgeSize = collector ? 84 : 56;
        var badge = new Border
        {
            Width = badgeSize,
            Height = badgeSize,
            CornerRadius = new CornerRadius(collector ? badgeSize / 2 : 8),
            BorderThickness = new Thickness(collector ? 3 : 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        BindBrush(badge, Border.BorderBrushProperty, unlocked ? "Accent" : "Stroke");
        if (badgeText is not null)
        {
            var badgeLabel = new TextBlock
            {
                Text = badgeText,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
                FontSize = badgeText.Length > 3 ? 14 : 20,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            BindBrush(badgeLabel, TextBlock.ForegroundProperty, unlocked ? "Accent" : "TextDim");
            badge.Child = badgeLabel;
        }
        else
        {
            Icon trophy = Icons.Make("Trophy", collector ? 40 : 26, unlocked ? "Accent" : "TextDim");
            trophy.HorizontalAlignment = HorizontalAlignment.Center;
            badge.Child = trophy;
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = AchievementName(achievement), FontSize = collector ? 22 : 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(new TextBlock
        {
            Text = unlocked ? AchievementHint(achievement) : Strings.Get("Ach_HiddenHint"),
            FontSize = 12.5,
            LineHeight = 18,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "dim" }
        });
        if (collector && unlocked)
        {
            int total = Enum.GetValues<Achievement>().Length;
            words.Children.Add(new TextBlock { Text = Strings.Format("Ach_Count", total, total), FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0), Classes = { "accent" } });
        }

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = collector ? 20 : 16 };
        body.Children.Add(badge);
        Grid.SetColumn(words, 1);
        body.Children.Add(words);
        var bodyBox = new Border { Child = body, Padding = collector ? new Thickness(22, 20) : new Thickness(18, 16) };
        BindBrush(bodyBox, Border.BackgroundProperty, "Window");

        var card = new Border
        {
            Child = new StackPanel { Children = { bannerBox, bodyBox } },
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        BindBrush(card, Border.BorderBrushProperty, "Stroke");
        return card;
    }

    /// <summary>Binds a brush property to a theme resource so the card follows a theme change.</summary>
    /// <param name="target">The control to bind on.</param>
    /// <param name="property">The brush property.</param>
    /// <param name="resourceKey">The theme brush key, such as "Accent".</param>
    private static void BindBrush(AvaloniaObject target, AvaloniaProperty property, string resourceKey)
    {
        if (Application.Current is { } app)
        {
            target.Bind(property, app.GetResourceObservable(resourceKey));
        }
    }

    /// <summary>Lines an unlock up for the toast; several at once show one after another.</summary>
    /// <param name="achievement">The one just unlocked.</param>
    private void QueueToast(Achievement achievement)
    {
        _toastQueue.Enqueue(achievement);
        if (!AchievementToast.IsVisible)
        {
            ShowNextToast();
        }
    }

    /// <summary>Shows the next queued unlock for seven seconds, or nothing when the queue is empty.</summary>
    private void ShowNextToast()
    {
        if (_toastQueue.Count == 0)
        {
            return;
        }
        Achievement achievement = _toastQueue.Dequeue();
        ToastKicker.Text = Strings.Get("Dialog_AchievementUnlocked").ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        ToastName.Text = AchievementName(achievement);
        ToastHint.Text = AchievementHint(achievement);
        AchievementToast.IsVisible = true;
        _toastTimer.Start();
    }

    /// <summary>Hides the toast, on its timer or a click, and shows the next one if any is waiting.</summary>
    private void DismissToast()
    {
        _toastTimer.Stop();
        AchievementToast.IsVisible = false;
        ShowNextToast();
    }

    /// <summary>Judges a finished scan off the UI thread and unlocks what it earned; also counts the scans of this root for Regular.</summary>
    /// <param name="root">The finished tree; read only.</param>
    /// <param name="path">The scanned root.</param>
    /// <param name="readFileTable">True when the file table was read rather than the folders walked.</param>
    private async Task JudgeScanAsync(FsNode root, string path, bool readFileTable)
    {
        if (_mock)
        {
            return;
        }
        bool isDrive = PlatformFiles.Current.IsVolumeRoot(path);
        (long total, long free) = DriveSizes(path, isDrive);
        bool fileTableAvailable = OperatingSystem.IsWindows() && isDrive && IsNtfs(path);
        var facts = new ScanFacts(isDrive, total, free, readFileTable, fileTableAvailable, _scanClock.Elapsed, _scanStartedLocal);

        List<Achievement> earned = await Task.Run(() => Achievements.ForScan(root, facts));
        Store.UnlockAll(earned);
        if (Store.Bump("scans:" + path) >= 10)
        {
            Store.Unlock(Achievement.Regular);
        }
    }

    /// <summary>Judges a comparison with the previous scan of the same root and unlocks what it earned.</summary>
    /// <param name="root">The new tree with its baseline sizes applied.</param>
    /// <param name="baseline">The previous scan's header.</param>
    /// <param name="comparison">The comparison, for the bytes gone.</param>
    private void JudgeComparison(FsNode root, ScanFileInfo baseline, ScanComparison comparison)
    {
        if (_mock)
        {
            return;
        }
        bool isDrive = PlatformFiles.Current.IsVolumeRoot(root.FullPath);
        (long total, long free) = DriveSizes(root.FullPath, isDrive);
        Store.UnlockAll(Achievements.ForComparison(root, baseline, comparison.GoneBytes, total, free));
    }

    /// <summary>Counts the views visited this session; all three earn Tourist.</summary>
    /// <param name="view">The view just shown.</param>
    private void NoteViewForAchievements(MainView view)
    {
        if (!_mock && _viewsSeen.Add(view) && _viewsSeen.Count == Enum.GetValues<MainView>().Length)
        {
            Store.Unlock(Achievement.Tourist);
        }
    }

    /// <summary>A saved scan was opened; one over a year old earns Archaeologist.</summary>
    /// <param name="info">The opened scan's header.</param>
    private void NoteOpenedScanForAchievements(ScanFileInfo info)
    {
        if (!_mock && DateTime.UtcNow - info.ScannedUtc > TimeSpan.FromDays(365))
        {
            Store.Unlock(Achievement.Archaeologist);
        }
    }

    /// <summary>A drive's total and free bytes now.</summary>
    /// <param name="path">The scanned root.</param>
    /// <param name="isDrive">False skips the lookup; folders have no sizes of their own.</param>
    /// <returns>Total and free, or zeros for a folder or a drive that cannot be read.</returns>
    private static (long Total, long Free) DriveSizes(string path, bool isDrive)
    {
        if (!isDrive)
        {
            return (0, 0);
        }
        try
        {
            var drive = new DriveInfo(path);
            return drive.IsReady ? (drive.TotalSize, drive.AvailableFreeSpace) : (0, 0);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A drive that is gone is judged as a folder.
            return (0, 0);
        }
    }
}
