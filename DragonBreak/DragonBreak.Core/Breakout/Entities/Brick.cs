using System;
using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Breakout.Entities;

public sealed class Brick
{
    public Rectangle Bounds;

    public int HitPoints;
    public readonly int MaxHitPoints;

    public readonly Color[] Palette;

    // -1 = shared/unowned (classic).
    public readonly int OwnerPlayerIndex;

    public bool IsAlive => HitPoints > 0;

    public Brick(Rectangle bounds, int hitPoints, Color[] palette, int ownerPlayerIndex = -1)
    {
        Bounds = bounds;
        MaxHitPoints = Math.Max(1, hitPoints);
        HitPoints = MaxHitPoints;
        Palette = palette;
        OwnerPlayerIndex = ownerPlayerIndex;
    }

    public Color CurrentColor
    {
        get
        {
            if (HitPoints <= 0) return Color.Transparent;

            // Palette is ordered from "toughest" -> "weakest".
            int idx = MaxHitPoints - HitPoints;
            if (idx < 0) idx = 0;
            if (idx >= Palette.Length) idx = Palette.Length - 1;
            return Palette[idx];
        }
    }

    public void Hit()
    {
        if (HitPoints > 0) HitPoints--;
    }
}
