using System;
using System.Configuration;
using Avalonia;
using Avalonia.Controls;

namespace CraftHub.Helpers;

/// <summary>
/// Remembers a window's size, position and maximized state across app runs. Settings are addressed
/// by name (<c>{prefix}Width</c> etc.) so each window gets its own independent geometry from one
/// shared implementation.
/// </summary>
public static class WindowGeometryHelper
{
    /// <summary>Call once from a window's constructor. Restores geometry on open and persists it on close.</summary>
    public static void Attach(Window window, string settingPrefix)
    {
        window.Opened += (_, _) => Restore(window, settingPrefix);
        window.Closing += (_, _) => Save(window, settingPrefix);
    }

    private static void Restore(Window window, string prefix)
    {
        var settings = Properties.Settings.Default;

        if (Get<bool>(settings, prefix + "Maximized"))
        {
            window.WindowState = WindowState.Maximized;
            return; // restoring raw W/H over a maximized window would fight the window manager
        }

        var width = Get<double>(settings, prefix + "Width");
        var height = Get<double>(settings, prefix + "Height");
        if (width > 0 && height > 0)
        {
            window.Width = width;
            window.Height = height;
        }

        var x = Get<int>(settings, prefix + "X");
        var y = Get<int>(settings, prefix + "Y");
        if (x != 0 || y != 0)
        {
            // A monitor that's since been unplugged would otherwise strand the window off-screen.
            if (IsOnAnyScreen(window, x, y))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(x, y);
            }
        }
    }

    private static void Save(Window window, string prefix)
    {
        var settings = Properties.Settings.Default;
        var maximized = window.WindowState == WindowState.Maximized;

        Set(settings, prefix + "Maximized", maximized);

        // Only record real geometry from a normal window — a minimized/maximized one would save
        // sizes that reopen wrong.
        if (window.WindowState == WindowState.Normal)
        {
            Set(settings, prefix + "Width", window.Width);
            Set(settings, prefix + "Height", window.Height);
            Set(settings, prefix + "X", window.Position.X);
            Set(settings, prefix + "Y", window.Position.Y);
        }

        settings.Save();
    }

    private static bool IsOnAnyScreen(Window window, int x, int y)
    {
        var screens = window.Screens;
        if (screens == null) return true; // can't verify — trust the stored position

        foreach (var screen in screens.All)
            if (screen.Bounds.Contains(new PixelPoint(x, y)))
                return true;

        return false;
    }

    private static T Get<T>(ApplicationSettingsBase settings, string key)
    {
        try { return settings[key] is T value ? value : default!; }
        catch (SettingsPropertyNotFoundException) { return default!; }
    }

    private static void Set(ApplicationSettingsBase settings, string key, object value)
    {
        try { settings[key] = value; }
        catch (SettingsPropertyNotFoundException) { /* window opted out of persistence */ }
    }
}
