#nullable enable
using System;

namespace DragonBreak.Core.Settings;

public sealed record UiSettings
{
    public bool ShowHud { get; init; } = true;

    /// <summary>
    /// Multiplier applied to HUD/menu font size when drawing.
    /// </summary>
    public float HudScale { get; init; } = 1.25f;

    /// <summary>
    /// Per-player HUD visibility toggles (useful for party/local multiplayer).
    /// If <see cref="ShowHud"/> is false, these are ignored.
    /// </summary>
    public bool ShowP1Hud { get; init; } = true;
    public bool ShowP2Hud { get; init; } = true;
    public bool ShowP3Hud { get; init; } = true;
    public bool ShowP4Hud { get; init; } = true;

    public static UiSettings Default => new()
    {
        ShowHud = true,
        HudScale = 1.25f,
        ShowP1Hud = true,
        ShowP2Hud = true,
        ShowP3Hud = true,
        ShowP4Hud = true,
    };

    public UiSettings Validate()
    {
        float scale = float.IsFinite(HudScale) ? HudScale : Default.HudScale;
        scale = Math.Clamp(scale, 0.75f, 3.0f);

        return this with
        {
            HudScale = scale,
        };
    }
}
