#nullable enable
using System;

namespace DragonBreak.Core.Input;

public enum TouchPhase
{
    Began,
    Moved,
    Ended,
    Canceled,
}

/// <summary>
/// A single touch point in normalized screen coordinates [0..1].
/// </summary>
public readonly record struct TouchPoint(
    int Id,
    TouchPhase Phase,
    float X01,
    float Y01,
    bool IsTap)
{
    public TouchPoint Normalize()
        => this with { X01 = Clamp01(X01), Y01 = Clamp01(Y01) };

    private static float Clamp01(float v)
        => v < 0f ? 0f : (v > 1f ? 1f : v);
}
