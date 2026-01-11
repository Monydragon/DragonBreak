#nullable enable
using System;

namespace DragonBreak.Core.Settings;

public sealed record GameplaySettings
{
    public DifficultyId Difficulty { get; init; } = DifficultyId.Normal;

    public ContinueMode ContinueMode { get; init; } = ContinueMode.PromptThenAuto;

    /// <summary>
    /// Used when <see cref="ContinueMode"/> is <see cref="Settings.ContinueMode.PromptThenAuto"/>.
    /// </summary>
    public float AutoContinueSeconds { get; init; } = 2.5f;

    public static GameplaySettings Default => new()
    {
        Difficulty = DifficultyId.Normal,
        ContinueMode = ContinueMode.PromptThenAuto,
        AutoContinueSeconds = 2.5f,
    };

    public GameplaySettings Validate()
    {
        float secs = float.IsFinite(AutoContinueSeconds) ? AutoContinueSeconds : Default.AutoContinueSeconds;
        secs = Math.Clamp(secs, 0.5f, 10f);

        return this with
        {
            AutoContinueSeconds = secs,
        };
    }
}

