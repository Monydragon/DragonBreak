using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Breakout.Entities;

public enum PowerUpType
{
    ExpandPaddle,
    SlowBall,
    FastBall,
    ExtraLife,
    ScoreBoost,
    MultiBall,
    ScoreBurst,
}

public sealed class PowerUp
{
    public PowerUpType Type;
    public Vector2 Position;
    public Vector2 Velocity;
    public Vector2 Size;

    public bool IsAlive = true;

    public PowerUp(PowerUpType type, Vector2 position)
    {
        Type = type;
        Position = position;
        Velocity = new Vector2(0f, 170f);
        Size = new Vector2(28f, 14f);
    }

    public Rectangle Bounds => new(
        (int)(Position.X - Size.X * 0.5f),
        (int)(Position.Y - Size.Y * 0.5f),
        (int)Size.X,
        (int)Size.Y);

    public void Update(float dtSeconds)
    {
        Position += Velocity * dtSeconds;
    }
}
