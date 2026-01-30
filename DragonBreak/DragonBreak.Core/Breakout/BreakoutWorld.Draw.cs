#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout;

public sealed partial class BreakoutWorld
{
    /// <summary>
    /// Draw gameplay elements (background, bricks, paddles, balls, powerups).
    /// UI/menus are rendered separately.
    /// </summary>
    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // Background
        sb.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), new Color(16, 16, 20));

        // Bricks
        for (int i = 0; i < _bricks.Count; i++)
        {
            if (!_bricks[i].IsAlive) continue;

            sb.Draw(_pixel, _bricks[i].Bounds, _bricks[i].CurrentColor);

            // Show remaining hits-to-break for multi-hit bricks.
            if (_hudFont != null && _bricks[i].HitPoints >= 2)
            {
                string hpText = _bricks[i].HitPoints.ToString();
                hpText = SafeText(hpText);

                float textScale = 0.75f;
                var size = _hudFont.MeasureString(hpText) * textScale;

                var b = _bricks[i].Bounds;
                float x = b.X + (b.Width - size.X) * 0.5f;
                float y = b.Y + (b.Height - size.Y) * 0.5f;

                Color fill = new Color(255, 210, 70);
                Color outline = Color.Black * 0.90f;

                var bc = _bricks[i].CurrentColor;
                int luma = (bc.R * 299 + bc.G * 587 + bc.B * 114) / 1000;
                if (luma > 220)
                    fill = new Color(60, 40, 20);

                var pos = new Vector2(x, y);

                // 8-way outline (1px) for visibility.
                sb.DrawString(_hudFont, hpText, pos + new Vector2(-1, 0), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(1, 0), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(0, -1), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(0, 1), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(-1, -1), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(-1, 1), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(1, -1), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                sb.DrawString(_hudFont, hpText, pos + new Vector2(1, 1), outline, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);

                // Main pass.
                sb.DrawString(_hudFont, hpText, pos, fill, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            }
        }

        // Paddles
        for (int i = 0; i < _paddles.Count; i++)
            sb.Draw(_pixel, _paddles[i].Bounds, PlayerBaseColors[Math.Clamp(i, 0, PlayerBaseColors.Length - 1)]);

        // --- Debug: AI overlay (playfield-local draw) ---
        bool debug = (_settings?.Current.Gameplay ?? Settings.GameplaySettings.Default).DebugMode;
        if (debug && _debugDrawAi)
        {
            EnsureAiArraysSized();

            for (int i = 0; i < _paddles.Count; i++)
            {
                if (!IsAiForPaddle(i))
                    continue;

                // Target marker
                if ((uint)i < (uint)_aiLastTargetByPaddle.Length)
                {
                    var t = _aiLastTargetByPaddle[i];
                    int tx = (int)MathF.Round(t.X);
                    int ty = (int)MathF.Round(t.Y);
                    var rect = new Rectangle(tx - 3, ty - 3, 7, 7);
                    sb.Draw(_pixel, rect, Color.Yellow);
                }

                // Label
                if (_hudFont != null)
                {
                    string label = $"AI P{i + 1}";
                    float s = 0.65f;
                    var pos = new Vector2(_paddles[i].Bounds.X, Math.Max(0, _paddles[i].Bounds.Y - (_hudFont.LineSpacing * s) - 2));
                    sb.DrawString(_hudFont, label, pos, Color.Yellow, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
                }
            }
        }

        // Balls
        for (int i = 0; i < _balls.Count; i++)
            sb.Draw(_pixel, _balls[i].Bounds, _balls[i].DrawColor ?? Color.White);

        // Powerups
        for (int i = 0; i < _powerUps.Count; i++)
            sb.Draw(_pixel, _powerUps[i].Bounds, Color.Gold);
    }
}
