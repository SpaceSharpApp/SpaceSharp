namespace SpaceSharp.Util;

/// <summary>XAML markup extension: <c>Text="{u:T Settings_Language}"</c>.</summary>
[System.Windows.Markup.MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : System.Windows.Markup.MarkupExtension
{
    public string Key { get; set; }
    public TExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
