#nullable enable

namespace DragonBreak.Core.Breakout;

/// <summary>
/// Stable, external representation of game modes.
/// This decouples persistence (like highscores) from BreakoutWorld's private enums.
/// </summary>
public enum GameModeId
{
    Arcade,
    Story,
    Puzzle,
}

