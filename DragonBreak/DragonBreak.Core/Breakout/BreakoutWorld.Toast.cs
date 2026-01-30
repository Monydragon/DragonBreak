#nullable enable
using System;
using DragonBreak.Core.Breakout.Entities;

namespace DragonBreak.Core.Breakout;

public sealed partial class BreakoutWorld
{
    // --- Toast helpers ---
    private void ShowToast(string text, float durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _toastText = text;
        _toastTimeLeft = Math.Max(_toastTimeLeft, durationSeconds);
    }

    private static string GetPowerUpToastText(PowerUpType type)
        => type switch
        {
            PowerUpType.ExpandPaddle => "Paddle expanded",
            PowerUpType.SlowBall => "Ball slowed",
            PowerUpType.FastBall => "Ball sped up",
            PowerUpType.MultiBall => "Multiball",
            PowerUpType.ScoreBoost => "Score x2",
            PowerUpType.ExtraLife => "+1 Life",
            PowerUpType.ScoreBurst => "+Score",
            _ => type.ToString(),
        };
}

