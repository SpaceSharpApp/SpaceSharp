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
using Velopack;

namespace SpaceSharp;

/// <summary>The About window.</summary>
public sealed class AboutWindow : Window
{
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";
    private readonly string _version = AppInfo.Version;

    /// <summary>Builds the window in code: a logo column, link groups on the right and a footer with Copy and Close.</summary>
    public AboutWindow()
    {
        Title = Strings.Get("About_AboutSpaceSharp");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp.ico")));
        TitleBarTheme.Attach(this);
        bool installed = Updater.Instance.IsInstalled;

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
        columns.Children.Add(BuildLeftColumn(installed));

        StackPanel right = BuildRightColumn(installed);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        root.Children.Add(columns);

        Border footer = BuildFooter();
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
    }

    /// <summary>The logo, name, version and tagline, with the copyright pinned to the bottom.</summary>
    /// <param name="installed">True for a Velopack install, which labels the build "installed" rather than "portable".</param>
    /// <returns>The filled left column with the panel background and a right-hand divider.</returns>
    private Border BuildLeftColumn(bool installed)
    {
        var left = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(24, 32, 24, 24) };
        left.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        left.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var leftStack = new DockPanel();
        var legal = new TextBlock { Text = $"{Copyright()}\n{Strings.Get("About_SourceSub")}", Classes = { "dim" }, FontSize = 11.5, LineHeight = 16, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(legal, Dock.Bottom);
        leftStack.Children.Add(legal);

        var intro = new StackPanel { Spacing = 8 };
        var logo = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png"))), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        intro.Children.Add(logo);
        intro.Children.Add(new TextBlock { Text = "SpaceSharp", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        string buildKind = Strings.Get(installed ? "About_BuildInstalled" : "About_BuildPortable");
        intro.Children.Add(new TextBlock { Text = $"{Strings.Format("About_Version", _version)}\n{buildKind}", Classes = { "dim" }, LineHeight = 18, TextWrapping = TextWrapping.Wrap });
        intro.Children.Add(new TextBlock { Text = Strings.Get("About_Tagline"), Classes = { "dim" }, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        leftStack.Children.Add(intro);

        left.Child = leftStack;
        return left;
    }

    /// <summary>The link groups and the system line.</summary>
    /// <param name="installed">True adds the "Check for updates" link, which only works for a Velopack install.</param>
    /// <returns>The right column: Version, Help and Project groups followed by the runtime and OS line.</returns>
    private StackPanel BuildRightColumn(bool installed)
    {
        var right = new StackPanel { Margin = new Thickness(28, 30, 28, 24), Spacing = 22 };

        StackPanel version = Group(Strings.Get("About_GroupVersion"));
        version.Children.Add(Link("Notes", Strings.Format("Update_WhatsNewIn", UpdateWindow.ShortVersion(_version)), () => _ = UpdateWindow.ShowWhatsNew(this, _version)));
        version.Children.Add(Link("Keyboard", Strings.Get("About_Shortcuts"), () => new ShortcutsWindow().ShowDialog(this)));
        version.Children.Add(Link("Trophy", Strings.Get("Ach_Title") + " (T)", () => new AchievementsWindow().ShowDialog(this)));
        if (installed)
        {
            _checkButton = Link("Sync", Strings.Get("About_CheckForUpdates"), null);
            _checkButton.Click += CheckUpdates_Click;
            version.Children.Add(_checkButton);
        }
        right.Children.Add(version);

        StackPanel help = Group(Strings.Get("About_GroupHelp"));
        help.Children.Add(Link("Bug", Strings.Get("About_ReportABug"), ReportBug));
        help.Children.Add(Link("Idea", Strings.Get("About_SuggestAFeature"), () => OpenUrl($"{Repository}/issues/new?template=feature_request.yml")));
        help.Children.Add(Link("ScanFolder", Strings.Get("About_CrashLogs"), () => OpenFolder(CrashLog.Folder)));
        right.Children.Add(help);

        StackPanel project = Group(Strings.Get("About_GroupProject"));
        project.Children.Add(Link("Globe", Strings.Get("About_Source"), () => OpenUrl(Repository)));
        project.Children.Add(Link("Book", Strings.Get("About_Credits"), () => new CreditsWindow().ShowDialog(this)));
        right.Children.Add(project);

        right.Children.Add(new TextBlock { Text = SystemDescription(), Classes = { "dim" }, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        return right;
    }

    /// <summary>The bottom bar with the Copy version info and Close buttons.</summary>
    /// <returns>A top-bordered strip with the two buttons right-aligned; Close is both the default and the cancel button.</returns>
    private Border BuildFooter()
    {
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12) };
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Classes = { "tool" }, Content = Icons.WithText("Copy", Strings.Get("About_CopyVersionInfo")) };
        copy.Click += CopyInfo_Click;
        var close = new Button { Classes = { "primary" }, Content = Strings.Get("About_Close"), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(copy);
        buttons.Children.Add(close);

        footer.Child = buttons;
        return footer;
    }

    /// <summary>The copyright line baked into the assembly by the csproj.</summary>
    /// <returns>The AssemblyCopyright text, or an empty string when the attribute is missing.</returns>
    private static string Copyright() => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>A link group with a small uppercase heading; links are added by the caller.</summary>
    /// <param name="heading">Localized group name; shown in uppercase with letter spacing.</param>
    /// <returns>A stack holding only the heading so far.</returns>
    private static StackPanel Group(string heading)
    {
        var group = new StackPanel { Spacing = 4 };
        group.Children.Add(new TextBlock { Text = heading.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Classes = { "dim" }, Margin = new Thickness(0, 0, 0, 6) });
        return group;
    }

    /// <summary>A text-link button with an icon in front.</summary>
    /// <param name="glyph">Icon name as known to <see cref="Icons"/>, drawn at 16 px in the dim text color.</param>
    /// <param name="text">Label after the icon, already localized.</param>
    /// <param name="onClick">Runs on the UI thread when the link is clicked; null leaves the Click event for the caller to wire.</param>
    /// <returns>The button, left-aligned, styled as a text link.</returns>
    private static Button Link(string glyph, string text, Action? onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(Icons.Make(glyph, 16, "TextDim"));
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Classes = { "textlink" }, Content = row, HorizontalAlignment = HorizontalAlignment.Left };
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }
        return button;
    }

    private Button? _checkButton;

    private async void CheckUpdates_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_checkButton is null)
        {
            return;
        }
        _checkButton.IsEnabled = false;
        SetLinkText(_checkButton, Strings.Get("About_Checking"));
        UpdateInfo? update = await Updater.Instance.CheckAsync();
        if (update is null)
        {
            SetLinkText(_checkButton, Strings.Get("About_UpToDate"));
            return;
        }

        // From here on the link installs the update instead of checking again.
        SetLinkText(_checkButton, Strings.Format("About_InstallAndRestart", Updater.Instance.AvailableVersion ?? string.Empty));
        _checkButton.IsEnabled = true;
        _checkButton.Click -= CheckUpdates_Click;
        _checkButton.Click += async (_, _) => await UpdateWindow.Show(this);
        await UpdateWindow.Show(this);
    }

    /// <summary>Replaces the label of a button made by <see cref="Link"/>; any other content is left alone.</summary>
    /// <param name="link">A button whose content is the icon-and-label row <see cref="Link"/> builds.</param>
    /// <param name="text">New label text.</param>
    private static void SetLinkText(Button link, string text)
    {
        if (link.Content is StackPanel row && row.Children.Count > 1 && row.Children[1] is TextBlock label)
        {
            label.Text = text;
        }
    }

    /// <summary>Shows a folder in the file manager; failures show an error dialog instead of throwing.</summary>
    /// <param name="folder">Full path of an existing folder.</param>
    private void OpenFolder(string folder)
    {
        try
        {
            Trash.Open(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _ = Dialog.Error(this, "SpaceSharp", ex.Message);
        }
    }

    /// <summary>The runtime and operating system in one line, for the About text and bug reports.</summary>
    /// <returns>Localized ".NET x on Windows y" text followed by the process architecture in parentheses, lowercase.</returns>
    private static string SystemDescription() =>
        Strings.Format("About_SystemLine", RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription) + " " +
        $"({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";

    private async void CopyInfo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard)
            {
                await ClipboardText.SetAsync(clipboard, $"SpaceSharp {_version} (Avalonia){Environment.NewLine}{SystemDescription()}");
            }
        }
        catch (Exception ex)
        {
            await Dialog.Error(this, Strings.Get("About_CopyFailed"), ex.Message);
        }
    }

    /// <summary>Opens the bug report form with version and system prefilled.</summary>
    private void ReportBug() =>
        OpenUrl($"{Repository}/issues/new?template=bug_report.yml" +
                $"&version={Uri.EscapeDataString(_version)}" +
                $"&windows={Uri.EscapeDataString(SystemDescription())}" +
                $"&install={Uri.EscapeDataString(Updater.Instance.IsInstalled ? "Installed (Velopack) · Avalonia" : "Portable · Avalonia")}");

    /// <summary>Opens a web address in the default browser; failures show an error dialog instead of throwing.</summary>
    /// <param name="url">Absolute http(s) address.</param>
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
