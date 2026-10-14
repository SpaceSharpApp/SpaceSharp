namespace SpaceSharp.Util;

/// <summary>
/// What a file is, by extension. The order is the order of colors in a palette; Other stays last. The six after
/// Code came out of Other in 2.0 so that a gaming PC does not read as "58% other".
/// </summary>
public enum FileCategory
{
    Images,
    Video,
    Audio,
    Archives,
    Programs,
    Documents,
    Code,
    GameData,
    Installers,
    DiskImages,
    Databases,
    Fonts,
    System,
    Other
}

/// <summary>Which window theme a palette is made for. The map background differs, so a palette rarely suits both.</summary>
public enum PaletteTheme
{
    Dark,
    Light,
    /// <summary>Not tagged (a custom palette without a "theme" line): listed under both.</summary>
    Any
}
