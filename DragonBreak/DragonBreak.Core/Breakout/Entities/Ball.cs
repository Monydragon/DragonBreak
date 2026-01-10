using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Breakout.Entities;

public sealed class Ball
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Radius;

    // Which local player owns/controls this ball.
    public int OwnerPlayerIndex;

    public Ball(Vector2 position, float radius, int ownerPlayerIndex = 0)
    {
        Position = position;
        Radius = radius;
        OwnerPlayerIndex = ownerPlayerIndex;
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
