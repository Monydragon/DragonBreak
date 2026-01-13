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

    /// <summary>
    /// Powerup drop chance per brick break, by difficulty.
    /// Easier difficulties should generally be higher.
    /// </summary>
    public PowerUpDropChances PowerUpDrops { get; init; } = PowerUpDropChances.Default;

    public static GameplaySettings Default => new()
    {
        Difficulty = DifficultyId.Normal,
        LevelSeed = 1337,
        ContinueMode = ContinueMode.PromptThenAuto,
        AutoContinueSeconds = 2.5f,
        PowerUpDrops = PowerUpDropChances.Default,
    };

    public float GetPowerUpDropChance(DifficultyId difficulty)
        => PowerUpDrops.GetChance(difficulty);

    public GameplaySettings Validate()
    {
        float secs = float.IsFinite(AutoContinueSeconds) ? AutoContinueSeconds : Default.AutoContinueSeconds;
        secs = Math.Clamp(secs, 0.5f, 10f);

        // Keep seed stable and within a simple safe range.
        // (We allow negatives, but clamp away extreme values that can be annoying in UI.)
        int seed = LevelSeed;
        if (seed == 0) seed = Default.LevelSeed;
        seed = Math.Clamp(seed, -1_000_000_000, 1_000_000_000);

        var drops = (PowerUpDrops ?? PowerUpDropChances.Default).Validate();

        return this with
        {
            AutoContinueSeconds = secs,
            LevelSeed = seed,
            PowerUpDrops = drops,
        };
    }
}

public sealed record PowerUpDropChances
{
    public float Casual { get; init; } = 0.22f;
    public float VeryEasy { get; init; } = 0.20f;
    public float Easy { get; init; } = 0.18f;
    public float Normal { get; init; } = 0.16f;
    public float Hard { get; init; } = 0.14f;
    public float VeryHard { get; init; } = 0.12f;
    public float Extreme { get; init; } = 0.10f;

    public static PowerUpDropChances Default => new();

    public float GetChance(DifficultyId difficulty)
        => difficulty switch
        {
            DifficultyId.Casual => Casual,
            DifficultyId.VeryEasy => VeryEasy,
            DifficultyId.Easy => Easy,
            DifficultyId.Normal => Normal,
            DifficultyId.Hard => Hard,
            DifficultyId.VeryHard => VeryHard,
            DifficultyId.Extreme => Extreme,
            _ => Normal,
        };

    public PowerUpDropChances Validate()
    {
        float Clamp01(float v, float fallback)
        {
            if (!float.IsFinite(v)) v = fallback;
            return Math.Clamp(v, 0f, 1f);
        }

        var d = Default;
        return this with
        {
            Casual = Clamp01(Casual, d.Casual),
            VeryEasy = Clamp01(VeryEasy, d.VeryEasy),
            Easy = Clamp01(Easy, d.Easy),
            Normal = Clamp01(Normal, d.Normal),
            Hard = Clamp01(Hard, d.Hard),
            VeryHard = Clamp01(VeryHard, d.VeryHard),
            Extreme = Clamp01(Extreme, d.Extreme),
        };
    }
}
