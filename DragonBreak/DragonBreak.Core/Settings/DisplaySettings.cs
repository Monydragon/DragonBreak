#nullable enable
using System;

namespace DragonBreak.Core.Settings;

public sealed record DisplaySettings
{
    public WindowMode WindowMode { get; init; } = WindowMode.Windowed;

    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;

    public bool VSync { get; init; } = true;

    public static DisplaySettings Default => new()
    {
        WindowMode = WindowMode.Windowed,
        Width = 1280,
        Height = 720,
        VSync = true,
    };

    public DisplaySettings Validate()
    {
        int w = Math.Clamp(Width, 320, 7680);
        int h = Math.Clamp(Height, 200, 4320);
        return this with { Width = w, Height = h };
    }
}
