#nullable enable
using System;
using DragonBreak.Core.Breakout;
using DragonBreak.Core.Settings;

namespace DragonBreak.Core.Highscores;

public enum HighScoreScope
{
    Local,
}

public readonly record struct HighScoreKey(GameModeId Mode, DifficultyId Difficulty)
{
    public override string ToString() => $"{Mode}/{Difficulty}";
}

public sealed record HighScoreEntry
{
    public string Name { get; init; } = "";
    public int Score { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public GameModeId Mode { get; init; } = GameModeId.Arcade;
    public DifficultyId Difficulty { get; init; } = DifficultyId.Normal;

    public int LevelReached { get; init; }
    public int Seed { get; init; }

    public HighScoreKey Key => new(Mode, Difficulty);

    public static HighScoreEntry Create(string name, int score, GameModeId mode, DifficultyId difficulty, int levelReached, int seed)
        => new()
        {
            Name = name ?? "",
            Score = score,
            Timestamp = DateTimeOffset.UtcNow,
            Mode = mode,
            Difficulty = difficulty,
            LevelReached = Math.Max(0, levelReached),
            Seed = seed,
        };
}

public sealed record HighScoreSettings
{
    public int MaxEntriesPerKey { get; init; } = 10;

    public static HighScoreSettings Default => new();

    public HighScoreSettings Validate()
        => this with
        {
            MaxEntriesPerKey = Math.Clamp(MaxEntriesPerKey, 3, 200),
        };
}

