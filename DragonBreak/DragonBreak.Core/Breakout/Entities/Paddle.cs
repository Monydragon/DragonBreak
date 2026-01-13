using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Breakout.Entities;

public sealed class Paddle
{
    public Vector2 Position;
    public readonly Vector2 Size;
    public float SpeedPixelsPerSecond;

    /// <summary>
    /// Paddle velocity in pixels/second, computed from the last Update.
    /// </summary>
    public Vector2 Velocity { get; private set; }

    public Paddle(Vector2 position, Vector2 size, float speedPixelsPerSecond)
    {
        Position = position;
        Size = size;
        SpeedPixelsPerSecond = speedPixelsPerSecond;
        Velocity = Vector2.Zero;
    }

    public Rectangle Bounds => new((int)Position.X, (int)Position.Y, (int)Size.X, (int)Size.Y);

    public Vector2 Center => Position + Size * 0.5f;

    public void Update(float dtSeconds, float moveX, float moveY, int worldWidth, float minY, float maxY)
    {
        var prev = Position;

        Position.X += moveX * SpeedPixelsPerSecond * dtSeconds;
        Position.Y -= moveY * SpeedPixelsPerSecond * dtSeconds;

        Position.X = MathHelper.Clamp(Position.X, 0f, worldWidth - Size.X);
        Position.Y = MathHelper.Clamp(Position.Y, minY, maxY);

        if (dtSeconds > 0f)
            Velocity = (Position - prev) / dtSeconds;
        else
            Velocity = Vector2.Zero;
    }
}
