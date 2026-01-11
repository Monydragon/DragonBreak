#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Settings;

public sealed class DisplayModeService
{
    public IReadOnlyList<ResolutionOption> GetSupportedResolutions(GraphicsDeviceManager graphics)
    {
        // MonoGame supported display modes depend on adapter; filter duplicates.
        var set = new HashSet<ResolutionOption>();

        try
        {
            var adapter = graphics.GraphicsDevice?.Adapter ?? GraphicsAdapter.DefaultAdapter;
            foreach (var mode in adapter.SupportedDisplayModes)
            {
                // Filter out tiny modes.
                if (mode.Width < 800 || mode.Height < 600)
                    continue;

                set.Add(new ResolutionOption(mode.Width, mode.Height));
            }
        }
        catch
        {
            // If adapter info isn't available yet, fall back to a safe list.
        }

        if (set.Count == 0)
        {
            set.Add(new ResolutionOption(1280, 720));
            set.Add(new ResolutionOption(1600, 900));
            set.Add(new ResolutionOption(1920, 1080));
        }

        var list = new List<ResolutionOption>(set);
        list.Sort(static (a, b) => (a.Width * a.Height).CompareTo(b.Width * b.Height));
        return list;
    }

    public void Apply(DisplaySettings display, GraphicsDeviceManager graphics, GameWindow window)
    {
        display = (display ?? DisplaySettings.Default).Validate();

        // VSync
        graphics.SynchronizeWithVerticalRetrace = display.VSync;

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Window resizing only makes sense on desktop.
            window.AllowUserResizing = display.WindowMode == Settings.WindowMode.Windowed;
        }

        // Apply mode
        switch (display.WindowMode)
        {
            case Settings.WindowMode.Windowed:
                TrySetBorderless(window, false);
                graphics.PreferredBackBufferWidth = display.Width;
                graphics.PreferredBackBufferHeight = display.Height;
                graphics.IsFullScreen = false;
                graphics.ApplyChanges();
                break;

            case Settings.WindowMode.BorderlessFullscreen:
                // Borderless: not exclusive fullscreen; match desktop resolution.
                graphics.IsFullScreen = false;
                TrySetBorderless(window, true);

                var bounds = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                graphics.PreferredBackBufferWidth = bounds.Width;
                graphics.PreferredBackBufferHeight = bounds.Height;
                graphics.ApplyChanges();
                break;

            case Settings.WindowMode.ExclusiveFullscreen:
                // Exclusive fullscreen: request fullscreen and backbuffer size.
                TrySetBorderless(window, false);
                graphics.PreferredBackBufferWidth = display.Width;
                graphics.PreferredBackBufferHeight = display.Height;
                graphics.IsFullScreen = true;
                graphics.ApplyChanges();
                break;
        }
    }

    private static void TrySetBorderless(GameWindow window, bool borderless)
    {
        // DesktopGL exposes this on GameWindow in newer MonoGame builds; guard for safety.
        try
        {
            window.IsBorderless = borderless;
        }
        catch
        {
            // Ignore if not supported on the current platform/build.
        }
    }
}
