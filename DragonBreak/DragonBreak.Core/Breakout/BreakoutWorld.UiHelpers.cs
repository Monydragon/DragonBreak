#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Breakout.Entities;
using DragonBreak.Core.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout;

public sealed partial class BreakoutWorld
{
    // --- Settings adjustment ---
    private void AdjustSettingsValue(ref GameSettings pending, int dir)
    {
        // Defensive: if settings manager isn't wired, do nothing.
        if (dir == 0)
            return;

        // Helpers
        static float Step(float value, float step, float min, float max, int dir)
        {
            float v = value + step * dir;
            return Math.Clamp(v, min, max);
        }

        static int StepInt(int value, int step, int min, int max, int dir)
        {
            int v = value + step * dir;
            return Math.Clamp(v, min, max);
        }

        var disp = pending.Display;
        var audio = pending.Audio;
        var ui = pending.Ui;
        var gameplay = pending.Gameplay;

        switch (_settingsItem)
        {
            case SettingsItem.VSync:
                pending = pending with { Display = disp with { VSync = !disp.VSync } };
                break;

            case SettingsItem.MasterVolume:
                pending = pending with { Audio = audio with { MasterVolume = Step(audio.MasterVolume, 0.05f, 0f, 1f, dir) } };
                break;
            case SettingsItem.BgmVolume:
                pending = pending with { Audio = audio with { BgmVolume = Step(audio.BgmVolume, 0.05f, 0f, 1f, dir) } };
                break;
            case SettingsItem.SfxVolume:
                pending = pending with { Audio = audio with { SfxVolume = Step(audio.SfxVolume, 0.05f, 0f, 1f, dir) } };
                break;

            case SettingsItem.HudEnabled:
                pending = pending with { Ui = ui with { ShowHud = !ui.ShowHud } };
                break;
            case SettingsItem.HudScale:
                pending = pending with { Ui = ui with { HudScale = Step(ui.HudScale, 0.05f, 0.75f, 2.0f, dir) } };
                break;

            case SettingsItem.ContinueMode:
            {
                int count = Enum.GetValues(typeof(ContinueMode)).Length;
                int m = ((int)gameplay.ContinueMode + dir) % count;
                if (m < 0) m += count;
                pending = pending with { Gameplay = gameplay with { ContinueMode = (ContinueMode)m } };
                break;
            }

            case SettingsItem.AutoContinueSeconds:
                pending = pending with { Gameplay = gameplay with { AutoContinueSeconds = Step(gameplay.AutoContinueSeconds, 0.25f, 0.5f, 10f, dir) } };
                break;

            case SettingsItem.LevelSeed:
                pending = pending with { Gameplay = gameplay with { LevelSeed = StepInt(gameplay.LevelSeed, 1, -1_000_000_000, 1_000_000_000, dir) } };
                break;

            case SettingsItem.DebugMode:
                pending = pending with { Gameplay = gameplay with { DebugMode = !gameplay.DebugMode } };
                break;

            // Note: WindowMode/Resolution are intentionally left as display-only in this simplified UI.
            // LevelSeedRandomize/LevelSeedReset are handled via confirm.
        }
    }

    // --- Drawing helpers (menu + HUD panels) ---
    private void DrawCenteredPanel(SpriteBatch sb, Viewport vp, Rectangle panel, Color bg, Color border)
    {
        sb.Draw(_pixel, panel, bg);
        // border
        sb.Draw(_pixel, new Rectangle(panel.X, panel.Y, panel.Width, 2), border);
        sb.Draw(_pixel, new Rectangle(panel.X, panel.Bottom - 2, panel.Width, 2), border);
        sb.Draw(_pixel, new Rectangle(panel.X, panel.Y, 2, panel.Height), border);
        sb.Draw(_pixel, new Rectangle(panel.Right - 2, panel.Y, 2, panel.Height), border);
    }

    private void DrawMenuLines(SpriteBatch sb, Viewport vp, IEnumerable<(string Text, bool Selected)> lines, float scale)
    {
        if (_hudFont == null)
            return;

        var lineH = _hudFont.LineSpacing * scale;
        float x;
        float y;

        // Find widest line for a simple centered panel.
        float maxW = 0f;
        int count = 0;
        foreach (var (t, _) in lines)
        {
            maxW = Math.Max(maxW, _hudFont.MeasureString(t).X * scale);
            count++;
        }

        float panelW = Math.Min(vp.Width - 40, Math.Max(360, maxW + 64));
        float panelH = Math.Min(vp.Height - 40, Math.Max(220, count * lineH + 56));

        var panel = new Rectangle(
            (int)((vp.Width - panelW) * 0.5f),
            (int)((vp.Height - panelH) * 0.5f),
            (int)panelW,
            (int)panelH);

        DrawCenteredPanel(sb, vp, panel, new Color(0, 0, 0, 200), new Color(255, 255, 255, 64));

        x = panel.X + 32;
        y = panel.Y + 28;

        foreach (var (t, selected) in lines)
        {
            var c = selected ? Color.White : new Color(210, 210, 210);
            var prefix = selected ? "> " : "  ";
            sb.DrawString(_hudFont, prefix + t, new Vector2(x, y), c, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            y += lineH;
        }
    }

    private void DrawHud(SpriteBatch sb, Viewport vp, float scale)
    {
        if (_hudFont == null)
            return;

        var ui = _settings?.Current.Ui ?? UiSettings.Default;
        if (!ui.ShowHud)
            return;

        int hudH = GetHudBarHeight(vp);

        // Background bar.
        sb.Draw(_pixel, new Rectangle(vp.X, vp.Y, vp.Width, hudH), new Color(0, 0, 0, 220));

        // --- Top row: Mode/Difficulty (right) + Level (center) ---
        string md = $"{CurrentModeId} / {_selectedDifficultyId}";
        md = SafeText(md);
        var mdScale = scale * 0.75f;
        var mdSize = _hudFont.MeasureString(md) * mdScale;
        sb.DrawString(_hudFont, md, new Vector2(vp.Width - mdSize.X - 16, 14), new Color(200, 200, 200), 0f, Vector2.Zero, mdScale, SpriteEffects.None, 0f);

        // Level centered
        string levelText = $"Level {_levelIndex + 1}";
        levelText = SafeText(levelText);
        var levelScale = scale * 0.9f;
        var levelSize = _hudFont.MeasureString(levelText) * levelScale;
        sb.DrawString(
            _hudFont,
            levelText,
            new Vector2((vp.Width - levelSize.X) * 0.5f, 10),
            Color.White,
            0f,
            Vector2.Zero,
            levelScale,
            SpriteEffects.None,
            0f);

        // --- Second row: Score/Lives ---
        int playersToDraw = _activePlayerCount;
        playersToDraw = Math.Min(playersToDraw, _scoreByPlayer.Count);
        playersToDraw = Math.Min(playersToDraw, _livesByPlayer.Count);

        if (playersToDraw <= 0)
            return;

        float startY = 44;
        float lineScale = scale * 0.8f;
        float lineH = _hudFont.LineSpacing * lineScale;

        float maxBottom = hudH - 6;

        const float sidePad = 16f;
        float leftX = sidePad;
        float rightX = vp.Width - sidePad;

        int leftRow = 0;
        int rightRow = 0;

        for (int p = 0; p < playersToDraw; p++)
        {
            bool isLeftSide = (p % 2) == 0; // P1,P3,...
            int row = isLeftSide ? leftRow++ : rightRow++;

            float y = startY + row * lineH;
            if (y + lineH > maxBottom)
                continue;

            int score = (p >= 0 && p < _scoreByPlayer.Count) ? _scoreByPlayer[p] : 0;
            int lives = (p >= 0 && p < _livesByPlayer.Count) ? _livesByPlayer[p] : 0;
            if (IsCasualNoLose) lives = 999;

            string line = playersToDraw == 1
                ? $"SCORE {score}   LIVES {lives}"
                : $"P{p + 1} {score}  LIVES {lives}";

            line = SafeText(line);
            var lineSize = _hudFont.MeasureString(line) * lineScale;

            float x = isLeftSide
                ? leftX
                : Math.Max(leftX, rightX - lineSize.X);

            var colColor = PlayerBaseColors[Math.Clamp(p, 0, PlayerBaseColors.Length - 1)];
            sb.DrawString(_hudFont, line, new Vector2(x, y), colColor, 0f, Vector2.Zero, lineScale, SpriteEffects.None, 0f);
        }

        // --- Pause button (top-center, under level) ---
        var pauseText = "PAUSE";
        var pauseScale = scale * 0.78f;
        var pauseSize = _hudFont.MeasureString(pauseText) * pauseScale;

        int w = (int)(pauseSize.X + 40);
        int h = (int)(pauseSize.Y + 22);

        // Place under the level line, centered.
        int pauseY = 10 + (int)(_hudFont.LineSpacing * (scale * 0.9f)) + 6;
        pauseY = Math.Clamp(pauseY, 10, Math.Max(10, GetHudBarHeight(vp) - h - 6));

        var pauseRect = new Rectangle((int)((vp.Width - w) * 0.5f), pauseY, w, h);

        DrawCenteredPanel(sb, vp, pauseRect, new Color(0, 0, 0, 120), new Color(255, 255, 255, 90));
        sb.DrawString(_hudFont, pauseText, new Vector2(pauseRect.X + 20, pauseRect.Y + 9), Color.White, 0f, Vector2.Zero, pauseScale, SpriteEffects.None, 0f);

        // --- Active power-ups (timers) ---
        {
            var entries = new List<(string Label, float Remaining, float Duration, Color Color)>(3);

            if (_paddleWidthTimeLeft > 0.01f)
                entries.Add(("PADDLE", _paddleWidthTimeLeft, 12f, new Color(120, 210, 255)));

            if (_ballSpeedTimeLeft > 0.01f)
            {
                string label = _ballSpeedMultiplier < 0.99f ? "SLOW" : (_ballSpeedMultiplier > 1.01f ? "FAST" : "SPEED");
                entries.Add((label, _ballSpeedTimeLeft, 10f, _ballSpeedMultiplier < 0.99f ? new Color(120, 235, 120) : new Color(255, 160, 90)));
            }

            if (_scoreMultiplierTimeLeft > 0.01f)
                entries.Add(("SCORE x2", _scoreMultiplierTimeLeft, 12f, new Color(255, 210, 90)));

            if (entries.Count > 0)
            {
                // Anchor area: right of the pause button, but keep clear of the top-right HUD text.
                const float padX = 14f;
                const float padRight = 16f;

                float xLeft = pauseRect.Right + padX;

                // Reserve space for the mode/difficulty line so we don't collide with it.
                // (This line is drawn at vp.Width - mdSize.X - 16.)
                float mdRightPad = 16f;
                float mdLeft = vp.Width - mdSize.X - mdRightPad;

                // Clamp our right edge to stay left of the mode/difficulty text, with a small gap.
                float xRightMax = Math.Min(vp.Width - padRight, mdLeft - 12f);

                // If the pause button is very wide (or viewport is narrow), fall back to a safe lane.
                if (xLeft > xRightMax - 80f)
                {
                    xLeft = Math.Max(padRight, pauseRect.Right + 6f);
                    xRightMax = Math.Max(xLeft + 80f, xRightMax);
                }

                // Keep the timers from hugging the far-right edge:
                // reserve a fixed lane width and place it immediately to the right of the pause button.
                // This shifts the whole block left on wide screens (like in your screenshot).
                float desiredLaneW = Math.Clamp(vp.Width * 0.22f, 170f, 260f);
                float maxLaneW = Math.Max(80f, xRightMax - xLeft);
                float laneW = Math.Min(desiredLaneW, maxLaneW);

                float xRight = Math.Min(xRightMax, xLeft + laneW);
                float maxPanelW = Math.Max(80f, xRight - xLeft);

                // Scale down slightly if needed so all rows fit inside the HUD bar.
                float powerScale = scale * 0.62f;
                float barH = Math.Max(6f, 6f * scale);

                float hudTop = 44f;
                float hudBottom = hudH - 8f;
                float availableH = Math.Max(1f, hudBottom - hudTop);

                // Try a few times to make everything fit; keep it deterministic and cheap.
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float rowH = Math.Max(barH + 10f, _hudFont.LineSpacing * powerScale);
                    float needed = rowH * entries.Count + rowH * 0.65f; // + header space

                    if (needed <= availableH)
                        break;

                    powerScale *= 0.9f;
                }

                float rowHFinal = Math.Max(barH + 10f, _hudFont.LineSpacing * powerScale);

                // Center the block vertically within the HUD bar.
                float blockH = rowHFinal * entries.Count;
                float yTop = hudTop + (availableH - blockH) * 0.5f;
                yTop = Math.Clamp(yTop, hudTop, Math.Max(hudTop, hudBottom - blockH));

                // Header
                string header = "POWER";
                var headerSize = _hudFont.MeasureString(header) * powerScale;
                float headerX = Math.Clamp(xRight - headerSize.X, xLeft, xRight);
                float headerY = yTop - rowHFinal * 0.65f;
                if (headerY >= hudTop - rowHFinal) // keep it reasonably inside the bar
                    sb.DrawString(_hudFont, header, new Vector2(headerX, headerY), new Color(220, 220, 220), 0f, Vector2.Zero, powerScale, SpriteEffects.None, 0f);

                float y = yTop;

                // Bar width is bounded by available panel width so it never draws under the pause button.
                float barW = Math.Clamp(maxPanelW, 110f, 240f);

                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];

                    float pct = e.Duration <= 0.01f ? 0f : MathHelper.Clamp(e.Remaining / e.Duration, 0f, 1f);

                    string text = $"{e.Label} {e.Remaining:0.0}s";
                    text = SafeText(text);
                    var size = _hudFont.MeasureString(text) * powerScale;

                    float textX = Math.Clamp(xRight - size.X, xLeft, xRight);
                    sb.DrawString(_hudFont, text, new Vector2(textX, y), e.Color, 0f, Vector2.Zero, powerScale, SpriteEffects.None, 0f);

                    // bar under the text
                    int bx = (int)Math.Clamp(xRight - barW, xLeft, xRight);
                    int by = (int)(y + size.Y + 2);
                    int bw = (int)Math.Clamp(barW, 1f, xRight - bx);
                    int bh = (int)(barH);

                    sb.Draw(_pixel, new Rectangle(bx, by, bw, bh), new Color(255, 255, 255, 35));
                    sb.Draw(_pixel, new Rectangle(bx, by, Math.Max(1, (int)(bw * pct)), bh), new Color((byte)e.Color.R, (byte)e.Color.G, (byte)e.Color.B, (byte)200));

                    y += rowHFinal;
                }
            }
        }
    }
}
