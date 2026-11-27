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

/// <summary>The filter panel: which face shows, and where.</summary>
internal sealed partial class FilterPanel
{
    /// <summary>Shows the rail or the drawer per the settings, building it on first use.</summary>
    internal void ApplyLayout()
    {
        if (DrawerLayout)
        {
            _rail.IsVisible = false;
            if (_drawerContent.Children.Count == 0)
            {
                BuildDrawer();
            }
        }
        else
        {
            _drawer.IsVisible = false;
            _drawerParked = false;
            if (_railContent.Children.Count == 0)
            {
                BuildRail();
            }
            _rail.IsVisible = AppSettings.Current.ShowFilterRail && Host.MapShowing;
        }
        Sync();
    }

    /// <summary>
    /// The filter works on the map, so its drawer or rail shows only there: leaving the map puts it away,
    /// and coming back brings it out again if it was open. The filter itself stays applied either way.
    /// </summary>
    internal void KeepWithMap()
    {
        bool onMap = Host.MapShowing;
        if (!DrawerLayout)
        {
            if (_railContent.Children.Count > 0)
            {
                _rail.IsVisible = onMap && AppSettings.Current.ShowFilterRail;
            }
            return;
        }

        if (!onMap && _drawer.IsVisible)
        {
            _drawerParked = true;
            _drawer.IsVisible = false;
        }
        else if (onMap && _drawerParked)
        {
            _drawerParked = false;
            _drawer.IsVisible = true;
            Sync();
        }
    }

    /// <summary>The Filter button: opens or closes the active face, going to the map first when another view is showing.</summary>
    internal void Toggle()
    {
        if (!Host.MapShowing)
        {
            // The filter works on the map: go there and bring it out.
            Host.ShowMap();
            ShowFace();
            return;
        }
        if (DrawerLayout)
        {
            bool opensDrawer = !_drawer.IsVisible;
            _drawer.IsVisible = opensDrawer;
            if (opensDrawer)
            {
                Sync();
                _drawerName?.Focus();
            }
        }
        else
        {
            AppSettings.Current.ShowFilterRail = !AppSettings.Current.ShowFilterRail;
            _rail.IsVisible = AppSettings.Current.ShowFilterRail;
            if (AppSettings.Current.ShowFilterRail)
            {
                _railName?.Focus();
            }
        }
        Sync();
    }

    /// <summary>Opens the active filter face and focuses its name box.</summary>
    internal void Focus()
    {
        if (!Host.MapShowing)
        {
            Host.ShowMap();
        }
        if (DrawerLayout)
        {
            _drawer.IsVisible = true;
            Sync();
            _drawerName?.Focus();
            _drawerName?.SelectAll();
        }
        else
        {
            if (!AppSettings.Current.ShowFilterRail)
            {
                AppSettings.Current.ShowFilterRail = true;
                _rail.IsVisible = true;
            }
            _railName?.Focus();
            _railName?.SelectAll();
        }
    }

    /// <summary>Makes the active filter face visible without moving focus: the drawer directly, the rail through its toggle.</summary>
    internal void ShowFace()
    {
        if (DrawerLayout)
        {
            if (!Host.MapShowing)
            {
                _drawerParked = true;   // it comes out when the map does
                return;
            }
            _drawer.IsVisible = true;
            Sync();
        }
        else if (!AppSettings.Current.ShowFilterRail)
        {
            Toggle();
        }
    }

    /// <summary>Closes the drawer and hands the keyboard back to the map.</summary>
    internal void CloseDrawer()
    {
        _drawer.IsVisible = false;
        Host.FocusMap();
    }

    /// <summary>True when a filter name box has keyboard focus.</summary>
    internal bool HasFocus => _railName is { IsFocused: true } || _drawerName is { IsFocused: true };
}
