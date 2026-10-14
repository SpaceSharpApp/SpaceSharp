using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// The About window of the Windows app: the icon and name, one list row per thing About can do, the copyright and
/// system line, and a footer with Copy version info and Close; the same rows as the Windows app's.
/// </summary>
public sealed class AboutWindow : Window
{
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";
    private readonly string _version = AppInfo.Version;
    private readonly StackPanel _rows = new();

    public AboutWindow()
    {
        Title = Strings.Get("About_AboutSpaceSharp");
        Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp.Avalonia/Assets/SpaceSharp-256.png")));
        TitleBarTheme.Attach(this);

        var root = new DockPanel();

        // footer
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 12) };
        footer.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var buttons = new DockPanel();
        var close = new Button { Classes = { "primary" }, Content = Strings.Get("About_Close"), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var copy = new Button { Classes = { "tool" }, Content = Icons.WithText("Copy", Strings.Get("About_CopyVersionInfo")), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += CopyInfo_Click;
        buttons.Children.Add(close); buttons.Children.Add(copy);
        footer.Child = buttons;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // header
        var body = new StackPanel { Margin = new Thickness(24, 26, 24, 18) };
        var head = new DockPanel();
        var logo = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp.Avalonia/Assets/SpaceSharp-256.png"))), Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        DockPanel.SetDock(logo, Dock.Left);
        head.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "SpaceSharp", FontSize = 24, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(new TextBlock { Text = Strings.Get("About_Tagline"), Classes = { "dim" }, Margin = new Thickness(0, 5, 0, 0) });
        head.Children.Add(titles);
        body.Children.Add(head);

        // the list: one row per thing About can do
        var card = new Border { Margin = new Thickness(0, 20, 0, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = _rows };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        body.Children.Add(card);

        string copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        body.Children.Add(new TextBlock { Text = $"{copyright}  ·  {SystemDescription()}", Classes = { "dim" }, FontSize = 11.5, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(body);
        Content = root;

        BuildRows();
    }

    // ------------------------------------------------------------ rows

    private Button? _checkButton;

    private void BuildRows()
    {
        _rows.Children.Clear();
        bool installed = Updater.Instance.IsInstalled;

        _checkButton = installed ? new Button { Classes = { "tool" }, Content = Strings.Get("About_Check"), Height = 30 } : null;
        if (_checkButton is not null) _checkButton.Click += CheckUpdates_Click;
        Row("Sync", Strings.Format("About_Version", _version), installed ? Strings.Get("About_InstalledCheck") : Strings.Get("About_PortableNoUpdates"), null, _checkButton);
        Row("Notes", Strings.Get("Update_WhatsNew"), Strings.Get("About_WhatsNewSub"), () => _ = UpdateWindow.ShowWhatsNew(this, _version));
        Row("Keyboard", Strings.Get("About_Shortcuts"), Strings.Get("About_ShortcutsSub"), () => new ShortcutsWindow().ShowDialog(this));
        Row("Bug", Strings.Get("About_ReportABug"), Strings.Get("About_ReportSub"), ReportBug);
        Row("Idea", Strings.Get("About_SuggestAFeature"), null, () => OpenUrl($"{Repository}/issues/new?template=feature_request.yml"));
        Row("ScanFolder", Strings.Get("About_CrashLogs"), Strings.Get("About_CrashLogsSub"), () => OpenFolder(CrashLog.Folder));
        Row("Globe", Strings.Get("About_Source"), Strings.Get("About_SourceSub"), () => OpenUrl(Repository));
        Row("Book", Strings.Get("About_Credits"), Strings.Get("About_CreditsSub"), () => _ = Dialog.Info(this, Strings.Get("About_Credits"), Strings.Get("About_CreditsText")));
    }

    private async void CheckUpdates_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_checkButton is null) return;
        _checkButton.IsEnabled = false;
        _checkButton.Content = Strings.Get("About_Checking");
        var update = await Updater.Instance.CheckAsync();
        if (update is null)
        {
            _checkButton.Content = Strings.Get("About_UpToDate");
            return;
        }

        // Same prompt as at startup: install, read what's new, or later.
        _checkButton.Content = Strings.Format("About_InstallAndRestart", Updater.Instance.AvailableVersion ?? string.Empty);
        _checkButton.IsEnabled = true;
        _checkButton.Click -= CheckUpdates_Click;
        _checkButton.Click += async (_, _) => await UpdateWindow.Show(this);
        await UpdateWindow.Show(this);
    }

    private void OpenFolder(string folder)
    {
        try { Trash.Open(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { _ = Dialog.Error(this, "SpaceSharp", ex.Message); }
    }

    /// <summary>One list row: glyph, title, optional sub-line; either a button at the right or the whole row is a button with a chevron.</summary>
    private void Row(string glyph, string title, string? sub, Action? onClick, Button? action = null)
    {
        var grid = new Grid { Margin = new Thickness(14, 0, 12, 0), MinHeight = 48, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var icon = Icons.Make(glyph, 16, "TextDim");
        icon.Margin = new Thickness(0, 0, 14, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 9, 0, 9) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13.5 });
        if (!string.IsNullOrEmpty(sub))
            text.Children.Add(new TextBlock { Text = sub, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon); grid.Children.Add(text);

        Control content = grid;
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
        }
        else if (onClick is not null)
        {
            var chevron = Icons.Make("ChevronRight", 12, "TextDim");
            chevron.Margin = new Thickness(10, 0, 2, 0);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);
            var button = new Button { Classes = { "row" }, Content = grid };
            button.Click += (_, _) => onClick();
            content = button;
        }

        if (_rows.Children.Count > 0)
        {
            var rule = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) };
            rule.Bind(Border.BackgroundProperty, this.GetResourceObservable("Stroke"));
            _rows.Children.Add(rule);
        }
        _rows.Children.Add(content);
    }

    private static string SystemDescription() =>
        Strings.Format("About_SystemLine", RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription) + " " +
        $"({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";

    private async void CopyInfo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync($"SpaceSharp {_version} (Avalonia){Environment.NewLine}{SystemDescription()}");
        }
        catch (Exception ex)
        {
            await Dialog.Error(this, Strings.Get("About_CopyFailed"), ex.Message);
        }
    }

    /// <summary>Opens the bug form with the version and system fields already filled in.</summary>
    private void ReportBug() =>
        OpenUrl($"{Repository}/issues/new?template=bug_report.yml" +
                $"&version={Uri.EscapeDataString(_version)}" +
                $"&windows={Uri.EscapeDataString(SystemDescription())}" +
                $"&install={Uri.EscapeDataString(Updater.Instance.IsInstalled ? "Installed (Velopack) · Avalonia" : "Portable · Avalonia")}");

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _ = Dialog.Error(this, "SpaceSharp", Strings.Format("About_BrowserFailed", url, ex.Message));
        }
    }
}
