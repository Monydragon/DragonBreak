using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Breakout.Entities;

public sealed class Ball
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Radius;

    // Which local player owns/controls this ball.
    public int OwnerPlayerIndex;

    /// <summary>
    /// True when this ball was spawned by a multiball powerup.
    /// Extra balls should never cost lives when lost.
    /// </summary>
    public bool IsExtraBall;

    /// <summary>
    /// If set, overrides the default draw color used for this ball.
    /// </summary>
    public Color? DrawColor;

    public Ball(Vector2 position, float radius, int ownerPlayerIndex = 0, bool isExtraBall = false)
    {
        Position = position;
        Radius = radius;
        OwnerPlayerIndex = ownerPlayerIndex;
        IsExtraBall = isExtraBall;
    }

    public Rectangle Bounds => new(
        (int)(Position.X - Radius),
        (int)(Position.Y - Radius),
        (int)(Radius * 2f),
        (int)(Radius * 2f));

    public void Update(float dtSeconds)
    {
        Position += Velocity * dtSeconds;
    }
}
