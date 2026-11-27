using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Views;

/// <summary>The window's controls the filter panel builds into and shows or hides. They are laid out in the window's XAML.</summary>
/// <param name="Box">The hidden text box whose text is the filter.</param>
/// <param name="Toggle">The Filter button in the path row.</param>
/// <param name="Rail">The rail under the path row.</param>
/// <param name="RailContent">Where the rail's controls go.</param>
/// <param name="Drawer">The drawer over the right of the map.</param>
/// <param name="DrawerContent">Where the drawer's sections go.</param>
/// <param name="DrawerFooter">The drawer's bottom row.</param>
/// <param name="DrawerClear">The Clear all button in the drawer's title row.</param>
internal sealed record FilterPanelParts(
    TextBox Box,
    Button Toggle,
    Border Rail,
    DockPanel RailContent,
    Border Drawer,
    StackPanel DrawerContent,
    DockPanel DrawerFooter,
    Button DrawerClear);
