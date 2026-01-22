namespace DragonBreak.Core.Input;

/// <summary>
/// Platform-provided pointer/touch state (normalized coordinates).
/// Core stays platform-agnostic; platform launchers may provide this.
/// </summary>
public readonly record struct TouchPointerState(
    bool IsActive,
    float X01,
    float Y01,
    bool IsTap)
{
    public static TouchPointerState None => new(false, 0f, 0f, false);

    public static TouchPointerState Active(float x01, float y01, bool isTap)
        => new(true, Clamp01(x01), Clamp01(y01), isTap);

    private static float Clamp01(float v)
        => v < 0f ? 0f : (v > 1f ? 1f : v);
}

