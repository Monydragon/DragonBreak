#nullable enable
using System;

namespace DragonBreak.Core.Settings;

public sealed record GameplaySettings
{
    public DifficultyId Difficulty { get; init; } = DifficultyId.Normal;

    /// <summary>
    /// Seed used for procedural level generation.
    /// Same seed + level + difficulty => same brick layout.
    /// </summary>
    public int LevelSeed { get; init; } = 1337;

    public ContinueMode ContinueMode { get; init; } = ContinueMode.PromptThenAuto;

    /// <summary>
    /// Used when <see cref="ContinueMode"/> is <see cref="Settings.ContinueMode.PromptThenAuto"/>.
    /// </summary>
    public float AutoContinueSeconds { get; init; } = 2.5f;

    public static GameplaySettings Default => new()
    {
        Difficulty = DifficultyId.Normal,
        LevelSeed = 1337,
        ContinueMode = ContinueMode.PromptThenAuto,
        AutoContinueSeconds = 2.5f,
    };

    public GameplaySettings Validate()
    {
        float secs = float.IsFinite(AutoContinueSeconds) ? AutoContinueSeconds : Default.AutoContinueSeconds;
        secs = Math.Clamp(secs, 0.5f, 10f);

        // Keep seed stable and within a simple safe range.
        // (We allow negatives, but clamp away extreme values that can be annoying in UI.)
        int seed = LevelSeed;
        if (seed == 0) seed = Default.LevelSeed;
        seed = Math.Clamp(seed, -1_000_000_000, 1_000_000_000);

        return this with
        {
            AutoContinueSeconds = secs,
            LevelSeed = seed,
        };
    }
}
