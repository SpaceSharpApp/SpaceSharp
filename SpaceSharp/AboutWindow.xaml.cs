using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Services;
using SpaceSharp.Util;
using SpaceSharp.Controls;

namespace SpaceSharp;

public partial class AboutWindow : Window
{
    private readonly string _version;

    public AboutWindow()
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);

        var assembly = Assembly.GetExecutingAssembly();
        _version = AppInfo.Version;
        TaglineText.Text = Strings.Get("About_Tagline");
        CopyrightText.Text = $"{assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty}  ·  {SystemDescription()}";

        BuildRows();
    }

    // ------------------------------------------------------------ rows

    private Button? _checkButton;

    private void BuildRows()
    {
        Rows.Children.Clear();
        bool installed = Updater.Instance.IsInstalled;

        _checkButton = installed ? new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("About_Check"), Height = 30 } : null;
        if (_checkButton is not null) _checkButton.Click += CheckUpdates_Click;
        Row("Sync", Strings.Format("About_Version", _version), installed ? Strings.Get("About_InstalledCheck") : Strings.Get("About_PortableNoUpdates"), _checkButton, null);
        Row("Notes", Strings.Get("Update_WhatsNew"), Strings.Get("About_WhatsNewSub"), null, () => UpdateWindow.ShowWhatsNew(this, _version));
        Row("Keyboard", Strings.Get("About_Shortcuts"), Strings.Get("About_ShortcutsSub"), null, () => new ShortcutsWindow { Owner = this }.ShowDialog());
        Row("Bug", Strings.Get("About_ReportABug"), Strings.Get("About_ReportSub"), null, () => ReportBug_Click(this, new RoutedEventArgs()));
        Row("Idea", Strings.Get("About_SuggestAFeature"), null, null, () => SuggestFeature_Click(this, new RoutedEventArgs()));
        Row("Globe", Strings.Get("About_Source"), Strings.Get("About_SourceSub"), null, () => OpenUrl(Repository));
        Row("Book", Strings.Get("About_Credits"), Strings.Get("About_CreditsSub"), null, () => Dialog.Info(this, Strings.Get("About_Credits"), Strings.Get("About_CreditsText")));
    }

    /// <summary>One list row: glyph, title, optional sub-line; either a button at the right or the whole row is a button with a chevron.</summary>
    private void Row(string glyph, string title, string? sub, Button? action, Action? onClick)
    {
        var grid = new Grid { Margin = new Thickness(14, 0, 12, 0), MinHeight = 48 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = Icons.Make(glyph, 16, "TextDim");
        icon.Margin = new Thickness(0, 0, 14, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 9, 0, 9) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13.5 });
        if (!string.IsNullOrEmpty(sub))
        {
            var subText = new TextBlock { Text = sub, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            subText.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            text.Children.Add(subText);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon); grid.Children.Add(text);

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
        }

        FrameworkElement content = grid;
        if (onClick is not null)
        {
            var button = new Button { Content = grid, Style = (Style)FindResource("ListRowButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => onClick();
            content = button;
        }

        if (Rows.Children.Count > 0)
        {
            var rule = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) };
            rule.SetResourceReference(Border.BackgroundProperty, "Stroke");
            Rows.Children.Add(rule);
        }
        Rows.Children.Add(content);
    }

    private static string SystemDescription() =>
        Strings.Format("About_SystemLine", RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription) + " " +
        $"({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";

    private void CopyInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"SpaceSharp {_version}{Environment.NewLine}{SystemDescription()}");
        }
        catch (Exception ex)
        {
            Dialog.Error(this, Strings.Get("About_CopyFailed"), ex.Message);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
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
        _checkButton.Click += (_, _) => UpdateWindow.Show(this);
        UpdateWindow.Show(this);
    }

    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(Repository);

    private void SuggestFeature_Click(object sender, RoutedEventArgs e) =>
        OpenUrl($"{Repository}/issues/new?template=feature_request.yml");

    /// <summary>Opens the bug form with the version and system fields already filled in.</summary>
    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        string install = Updater.Instance.IsInstalled ? "Setup.exe (one-click)" : "Portable SpaceSharp.exe";
        OpenUrl($"{Repository}/issues/new?template=bug_report.yml" +
                $"&version={Uri.EscapeDataString(_version)}" +
                $"&windows={Uri.EscapeDataString(SystemDescription())}" +
                $"&install={Uri.EscapeDataString(install)}");
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialog.Error(this, "SpaceSharp", Strings.Format("About_BrowserFailed", url, ex.Message));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
