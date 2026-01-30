#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Breakout.Ui;
using DragonBreak.Core.Input;
using DragonBreak.Core.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout;

public sealed partial class BreakoutWorld
{
    // Touch UI buttons
    private static Rectangle GetBackButtonRect(Viewport vp)
        => new(14, 14, Math.Clamp((int)(vp.Width * 0.18f), 120, 220), Math.Clamp((int)(vp.Height * 0.075f), 56, 90));

    private void DrawBackButton(SpriteBatch sb, Viewport vp, float scale, string label = "BACK")
    {
        if (_hudFont == null)
            return;

        var rect = GetBackButtonRect(vp);
        float s = Math.Max(1f, scale * 0.9f);

        DrawCenteredPanel(sb, vp, rect, new Color(0, 0, 0, 160), new Color(255, 255, 255, 120));

        var t = label;
        var size = _hudFont.MeasureString(t) * s;
        var pos = new Vector2(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + (rect.Height - size.Y) * 0.5f);
        sb.DrawString(_hudFont, t, pos, Color.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }

    private static bool TapInside(Viewport vp, float x01, float y01, Rectangle rect)
    {
        float x = x01 * vp.Width;
        float y = y01 * vp.Height;
        return rect.Contains((int)x, (int)y);
    }

    /// <summary>
    /// Draws UI elements (HUD + menus). This is called un-clipped and without the gameplay transform.
    /// </summary>
    public void DrawUi(SpriteBatch sb, Viewport vp)
    {
        var ui = _settings?.Current.Ui ?? UiSettings.Default;
        float scale = ui.HudScale;

        // Main menu should NOT show gameplay/background. Keep it clean.
        if (_mode == WorldMode.Menu || _mode == WorldMode.Settings || _mode == WorldMode.HighScores || _mode == WorldMode.NameEntry || _mode == WorldMode.GameOver || _mode == WorldMode.LevelInterstitial || _mode == WorldMode.Paused || _mode == WorldMode.DebugJumpLevel)
        {
            sb.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), new Color(10, 10, 14));
        }

        // HUD during gameplay
        if (_mode == WorldMode.Playing)
            DrawHud(sb, vp, scale);

        // Simple UI routing
        switch (_mode)
        {
            case WorldMode.Menu:
                DrawMainMenu(sb, vp, scale);
                break;
            case WorldMode.Settings:
                DrawSettingsMenu(sb, vp, scale);
                break;
            case WorldMode.LevelInterstitial:
                DrawInterstitial(sb, vp, scale);
                break;
            case WorldMode.Paused:
                DrawPausedMenu(sb, vp, scale);
                break;
            case WorldMode.DebugJumpLevel:
                DrawDebugJumpLevelMenu(sb, vp, scale);
                break;
            case WorldMode.HighScores:
                DrawHighScoresMenu(sb, vp, scale);
                break;
            case WorldMode.NameEntry:
                DrawNameEntryMenu(sb, vp, scale);
                break;
            case WorldMode.GameOver:
                DrawGameOverMenu(sb, vp, scale);
                break;
        }

        // Draw back button on touch-first screens.
        if (_mode is WorldMode.Settings or WorldMode.HighScores or WorldMode.Paused or WorldMode.DebugJumpLevel)
            DrawBackButton(sb, vp, GetMenuScale(vp, scale));

        // Power-up toast during gameplay and pause.
        if (_hudFont != null && !string.IsNullOrWhiteSpace(_toastText) && _toastTimeLeft > 0f)
        {
            string t = SafeText(_toastText);
            float toastScale = scale;
            var size = _hudFont.MeasureString(t) * toastScale;
            var pos = new Vector2((vp.Width - size.X) * 0.5f, vp.Height * 0.20f);

            var bg = new Rectangle((int)(pos.X - 18), (int)(pos.Y - 10), (int)(size.X + 36), (int)(size.Y + 20));
            DrawCenteredPanel(sb, vp, bg, new Color(0, 0, 0, 200), new Color(255, 255, 255, 70));
            sb.DrawString(_hudFont, t, pos, Color.White, 0f, Vector2.Zero, toastScale, SpriteEffects.None, 0f);
        }
    }

    private void DrawMainMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        // Clean button-like list.
        var lines = new List<(string Text, bool Selected)>
        {
            ("START", _menuItem == MenuItem.Start),
            ($"PLAYERS: {_selectedPlayers}   <   >", _menuItem == MenuItem.Players),
            ($"DIFFICULTY: {_selectedDifficultyId}   <   >", _menuItem == MenuItem.Difficulty),
            ("HIGHSCORES", _menuItem == MenuItem.HighScores),
            ("SETTINGS", _menuItem == MenuItem.Settings),
        };

        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale) * 1.05f);
    }

    private void DrawSettingsMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        // Display pending values while editing so left/right changes are visible immediately.
        var live = _settings?.Pending ?? _settings?.Current ?? GameSettings.Default;

        string Pct(float v) => $"{(int)MathF.Round(Math.Clamp(v, 0f, 1f) * 100f)}%";

        var lines = new List<(string Text, bool Selected)>();
        lines.Add(($"MASTER VOL: {Pct(live.Audio.MasterVolume)}   <   >", _settingsItem == SettingsItem.MasterVolume));
        lines.Add(($"BGM VOL: {Pct(live.Audio.BgmVolume)}      <   >", _settingsItem == SettingsItem.BgmVolume));
        lines.Add(($"SFX VOL: {Pct(live.Audio.SfxVolume)}      <   >", _settingsItem == SettingsItem.SfxVolume));
        lines.Add(($"HUD: {(live.Ui.ShowHud ? "ON" : "OFF")}   <   >", _settingsItem == SettingsItem.HudEnabled));
        lines.Add(($"HUD SCALE: {live.Ui.HudScale:0.00}    <   >", _settingsItem == SettingsItem.HudScale));
        lines.Add(($"CONTINUE: {live.Gameplay.ContinueMode}   <   >", _settingsItem == SettingsItem.ContinueMode));
        lines.Add(($"AUTO CONT: {live.Gameplay.AutoContinueSeconds:0.00}s   <   >", _settingsItem == SettingsItem.AutoContinueSeconds));
        lines.Add(($"SEED: {live.Gameplay.LevelSeed}   <   >", _settingsItem == SettingsItem.LevelSeed));
        lines.Add(($"DEBUG: {(live.Gameplay.DebugMode ? "ON" : "OFF")}   <   >", _settingsItem == SettingsItem.DebugMode));
        lines.Add(("APPLY", _settingsItem == SettingsItem.Apply));
        lines.Add(("CANCEL", _settingsItem == SettingsItem.Cancel));

        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale));
    }

    private void DrawInterstitial(SpriteBatch sb, Viewport vp, float scale)
    {
        var lines = new List<(string Text, bool Selected)>
        {
            (SafeText(_levelInterstitialLine), false),
            ("", false),
            ("TAP / CONFIRM TO CONTINUE", true),
            ("BACK TO EXIT", false),
        };

        // Use phone-friendly menu scaling.
        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale));

        // Touch back button.
        DrawBackButton(sb, vp, GetMenuScale(vp, scale));
    }

    private void DrawPausedMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        // Ensure pause menu line list matches current debug mode.
        bool debug = (_settings?.Current.Gameplay ?? GameplaySettings.Default).DebugMode;
        _pauseMenu.SetDebugEnabled(debug);

        var lines = new List<(string Text, bool Selected)>();
        lines.Add(("PAUSED", true));
        lines.Add(("", false));
        foreach (var (label, selected) in _pauseMenu.GetLines())
            lines.Add((label.ToUpperInvariant(), selected));

        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale));
    }

    private void DrawHighScoresMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        if (_highScoresScreen == null)
            return;

        var lines = new List<(string Text, bool Selected)>();
        foreach (var (text, selected) in _highScoresScreen.GetLines(vp))
            lines.Add((text.ToUpperInvariant(), selected));

        // Use the same phone-friendly scale for touch readability.
        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale) * 0.95f);
    }

    private void DrawNameEntryMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        if (_nameEntryScreen == null)
            return;

        var lines = new List<(string Text, bool Selected)>();
        foreach (var (text, selected) in _nameEntryScreen.GetLines(vp))
            lines.Add((SafeText(text).ToUpperInvariant(), selected));

        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale) * 0.95f);
    }

    private void DrawGameOverMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        if (_gameOverScreen == null)
            return;

        var lines = new List<(string Text, bool Selected)>();
        foreach (var (text, selected) in _gameOverScreen.GetLines(vp))
            lines.Add((SafeText(text).ToUpperInvariant(), selected));

        DrawMenuLines(sb, vp, lines, GetMenuScale(vp, scale) * 0.90f);
    }

    private void DrawDebugJumpLevelMenu(SpriteBatch sb, Viewport vp, float scale)
    {
        if (_hudFont == null)
            return;

        float menuScale = GetMenuScale(vp, scale);

        var (l1, l2, l3) = _debugJumpLevelScreen.GetPromptLines();

        var lines = new List<(string Text, bool Selected)>
        {
            (l1, false),
            (l2, false),
            (l3, false),
            ("", false),
            ("Touch: left/right = +/-1   top/bottom = +/-10   center = confirm", false),
        };

        DrawMenuLines(sb, vp, lines, menuScale);
    }

    // --- Input / menu update (minimal, compiling) ---

    private void UpdateMenu(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        // Minimal navigation.
        var input = inputs.Length > 0 ? inputs[0] : default;

        const float deadzone = 0.55f;

        bool up = input.MenuUpPressed || input.MenuMoveY >= deadzone;
        bool down = input.MenuDownPressed || input.MenuMoveY <= -deadzone;

        bool leftHeld = input.MenuLeftHeld || input.MenuMoveX <= -deadzone;
        bool rightHeld = input.MenuRightHeld || input.MenuMoveX >= deadzone;

        bool confirm = input.MenuConfirmPressed || input.ServePressed;
        bool back = input.MenuBackPressed;

        // Touch: tap on menu lines to select + activate.
        if (input.Touches.TryGetBegan(out var tap) && tap.IsTap)
        {
            float menuScale = GetMenuScale(vp, (_settings?.Current.Ui ?? UiSettings.Default).HudScale) * 1.05f;
            float lineH = (_hudFont?.LineSpacing ?? 18) * menuScale;

            int count = 5;
            var panel = GetMenuPanelRect(vp, count, lineH);

            int row = HitTestMenuRow(vp, panel, lineH, tap.X01, tap.Y01);
            if (row >= 0)
            {
                _menuItem = row switch
                {
                    0 => MenuItem.Start,
                    1 => MenuItem.Players,
                    2 => MenuItem.Difficulty,
                    3 => MenuItem.HighScores,
                    4 => MenuItem.Settings,
                    _ => _menuItem,
                };

                confirm = true;
            }
        }

        // Touch back button.
        if (input.Touches.TryGetBegan(out var backTap) && backTap.IsTap)
        {
            if (TapInside(vp, backTap.X01, backTap.Y01, GetBackButtonRect(vp)))
            {
                back = true;
            }
        }

        if (back)
            return;

        if (up && !_menuUpConsumed)
        {
            _menuUpConsumed = true;
            _menuItem = PrevMenuItem(_menuItem);
        }
        if (!up) _menuUpConsumed = false;

        if (down && !_menuDownConsumed)
        {
            _menuDownConsumed = true;
            _menuItem = NextMenuItem(_menuItem);
        }
        if (!down) _menuDownConsumed = false;

        // Difficulty/Players use arrows
        if (_menuItem == MenuItem.Difficulty)
        {
            if (leftHeld && !_menuLeftConsumed)
            {
                _menuLeftConsumed = true;
                _selectedPresetIndex = Math.Clamp(_selectedPresetIndex - 1, 0, Presets.Length - 1);
                _selectedDifficultyId = PresetIndexToDifficulty(_selectedPresetIndex);
            }
            if (rightHeld && !_menuRightConsumed)
            {
                _menuRightConsumed = true;
                _selectedPresetIndex = Math.Clamp(_selectedPresetIndex + 1, 0, Presets.Length - 1);
                _selectedDifficultyId = PresetIndexToDifficulty(_selectedPresetIndex);
            }
        }
        else if (_menuItem == MenuItem.Players)
        {
            if (leftHeld && !_menuLeftConsumed)
            {
                _menuLeftConsumed = true;
                _selectedPlayers = Math.Clamp(_selectedPlayers - 1, 1, 4);
            }
            if (rightHeld && !_menuRightConsumed)
            {
                _menuRightConsumed = true;
                _selectedPlayers = Math.Clamp(_selectedPlayers + 1, 1, 4);
            }
        }

        if (!leftHeld) _menuLeftConsumed = false;
        if (!rightHeld) _menuRightConsumed = false;

        if (confirm)
        {
            switch (_menuItem)
            {
                case MenuItem.Start:
                    _preset = Presets[_selectedPresetIndex];
                    StartNewGame(vp, _preset);
                    _mode = WorldMode.Playing;
                    break;
                case MenuItem.Settings:
                    _settings?.BeginEdit();

                    // Start settings navigation at the top.
                    _settingsSelectedIndex = 0;
                    SyncSettingsItemFromSelectedIndexUi();

                    // Reset left/right repeat state.
                    _settingsAdjustRepeatTime = 0f;
                    _settingsLeftConsumed = false;
                    _settingsRightConsumed = false;

                    _mode = WorldMode.Settings;
                    break;
                case MenuItem.HighScores:
                    ShowHighScores(WorldMode.Menu);
                    break;
                case MenuItem.Players:
                    // No-op: left/right changes players.
                    break;
            }
        }
    }

    private static MenuItem NextMenuItem(MenuItem item)
        => item switch
        {
            MenuItem.Start => MenuItem.Players,
            MenuItem.Players => MenuItem.Difficulty,
            MenuItem.Difficulty => MenuItem.HighScores,
            MenuItem.HighScores => MenuItem.Settings,
            MenuItem.Settings => MenuItem.Start,
            _ => MenuItem.Start,
        };

    private static MenuItem PrevMenuItem(MenuItem item)
        => item switch
        {
            MenuItem.Start => MenuItem.Settings,
            MenuItem.Settings => MenuItem.HighScores,
            MenuItem.HighScores => MenuItem.Difficulty,
            MenuItem.Difficulty => MenuItem.Players,
            MenuItem.Players => MenuItem.Start,
            _ => MenuItem.Start,
        };

    // Settings UI: keep navigation/touch aligned with what we actually draw.
    private static readonly SettingsItem[] SettingsMenuItemsVisibleTopToBottom =
    [
        SettingsItem.MasterVolume,
        SettingsItem.BgmVolume,
        SettingsItem.SfxVolume,
        SettingsItem.HudEnabled,
        SettingsItem.HudScale,
        SettingsItem.ContinueMode,
        SettingsItem.AutoContinueSeconds,
        SettingsItem.LevelSeed,
        SettingsItem.DebugMode,
        SettingsItem.Apply,
        SettingsItem.Cancel,
    ];

    private void SyncSettingsItemFromSelectedIndexUi()
    {
        if (SettingsMenuItemsVisibleTopToBottom.Length == 0)
        {
            _settingsSelectedIndex = 0;
            return;
        }

        _settingsSelectedIndex = Math.Clamp(_settingsSelectedIndex, 0, SettingsMenuItemsVisibleTopToBottom.Length - 1);
        _settingsItem = SettingsMenuItemsVisibleTopToBottom[_settingsSelectedIndex];
    }

    private void UpdateSettingsMenu(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        var input = inputs.Length > 0 ? inputs[0] : default;

        const float deadzone = 0.55f;

        bool up = input.MenuUpPressed || input.MenuMoveY >= deadzone;
        bool down = input.MenuDownPressed || input.MenuMoveY <= -deadzone;

        bool leftHeld = input.MenuLeftHeld || input.MenuMoveX <= -deadzone;
        bool rightHeld = input.MenuRightHeld || input.MenuMoveX >= deadzone;

        bool confirm = input.MenuConfirmPressed;
        bool back = input.MenuBackPressed;

        // Touch back button (top-left)
        if (input.Touches.TryGetBegan(out var backTap) && backTap.IsTap)
        {
            if (TapInside(vp, backTap.X01, backTap.Y01, GetBackButtonRect(vp)))
                back = true;
        }

        // Touch support.
        ApplyTouchToSettingsMenu(vp, input, ref leftHeld, ref rightHeld, ref confirm);

        if (up && !_menuUpConsumed)
        {
            _menuUpConsumed = true;
            _settingsSelectedIndex = Math.Max(0, _settingsSelectedIndex - 1);
            SyncSettingsItemFromSelectedIndexUi();
        }
        if (!up) _menuUpConsumed = false;

        if (down && !_menuDownConsumed)
        {
            _menuDownConsumed = true;
            _settingsSelectedIndex = Math.Min(SettingsMenuItemsVisibleTopToBottom.Length - 1, _settingsSelectedIndex + 1);
            SyncSettingsItemFromSelectedIndexUi();
        }
        if (!down) _menuDownConsumed = false;

        // Value adjustment: initial step on press + repeat while held.
        // This makes sticks usable for changing volume/scale etc.
        int dir = 0;
        bool anyHeld = leftHeld || rightHeld;

        if (!anyHeld)
        {
            _settingsAdjustRepeatTime = 0f;
            _settingsLeftConsumed = false;
            _settingsRightConsumed = false;
        }
        else
        {
            // First adjustment happens immediately when direction becomes held.
            if (leftHeld && !_settingsLeftConsumed)
            {
                _settingsLeftConsumed = true;
                _settingsRightConsumed = false;
                _settingsAdjustRepeatTime = SettingsAdjustRepeatInitialDelay;
                dir = -1;
            }
            else if (rightHeld && !_settingsRightConsumed)
            {
                _settingsRightConsumed = true;
                _settingsLeftConsumed = false;
                _settingsAdjustRepeatTime = SettingsAdjustRepeatInitialDelay;
                dir = 1;
            }
            else
            {
                // Repeat while still held.
                _settingsAdjustRepeatTime -= dt;
                if (_settingsAdjustRepeatTime <= 0f)
                {
                    _settingsAdjustRepeatTime += SettingsAdjustRepeatInterval;
                    dir = rightHeld ? 1 : -1;
                }
            }
        }

        if (_settings != null)
        {
            if (_settings.Pending == null)
                _settings.BeginEdit();

            if (dir != 0)
            {
                var pending = _settings.Pending ?? _settings.Current;
                AdjustSettingsValue(ref pending, dir);
                _settings.SetPending(pending);
            }

            if (confirm)
            {
                if (_settingsItem == SettingsItem.Apply)
                {
                    _settings.ApplyPending();
                    _mode = WorldMode.Menu;
                }
                else if (_settingsItem == SettingsItem.Cancel)
                {
                    _settings.CancelEdit();
                    _mode = WorldMode.Menu;
                }
            }

            if (back)
            {
                _settings.CancelEdit();
                _mode = WorldMode.Menu;
            }
        }
        else
        {
            // No settings service wired: just back out.
            if (back || confirm)
                _mode = WorldMode.Menu;
        }
    }

    private static int HitTestMenuRow(Viewport vp, Rectangle panel, float lineH, float x01, float y01)
    {
        float x = x01 * vp.Width;
        float y = y01 * vp.Height;
        if (!panel.Contains((int)x, (int)y))
            return -1;

        float y0 = panel.Y + 28;
        int row = (int)MathF.Floor((y - y0) / Math.Max(1f, lineH));
        return row;
    }

    private bool TryConsumePauseMenuTouch(Viewport vp, in DragonBreakInput input, float uiScale, out PauseMenuScreen.PauseAction action)
    {
        action = PauseMenuScreen.PauseAction.None;

        if (!input.Touches.TryGetBegan(out var tap) || !tap.IsTap)
            return false;

        float menuScale = GetMenuScale(vp, uiScale);
        float lineH = (_hudFont?.LineSpacing ?? 18) * menuScale;

        int itemCount = _pauseMenu.GetItemCount();
        int lineCount = 2 + itemCount; // "PAUSED" + blank + items
        var panel = GetMenuPanelRect(vp, lineCount, lineH);

        int row = HitTestMenuRow(vp, panel, lineH, tap.X01, tap.Y01);
        if (row < 0)
            return false;

        int item = row - 2;
        action = _pauseMenu.GetActionForItemIndex(item);
        return action != PauseMenuScreen.PauseAction.None;
    }

    private static Rectangle GetMenuPanelRect(Viewport vp, int lineCount, float lineH)
    {
        // Big portrait-friendly panel (near full height/width).
        float portraitW = vp.Width * 0.92f;
        float portraitH = vp.Height * 0.82f;

        float panelW = Math.Min(vp.Width - 24, Math.Max(320, portraitW));
        float panelH = Math.Min(vp.Height - 24, Math.Max(260, Math.Min(portraitH, lineCount * lineH + 96)));

        return new Rectangle(
            (int)((vp.Width - panelW) * 0.5f),
            (int)((vp.Height - panelH) * 0.5f),
            (int)panelW,
            (int)panelH);
    }

    private static float GetMenuScale(Viewport vp, float baseScale)
    {
        // Make menus feel like "phone UI".
        bool portrait = vp.Height >= vp.Width;

        float s = baseScale;
        s = MathF.Max(s, portrait ? 3.0f : 2.1f);
        return MathHelper.Clamp(s, 1.6f, 3.4f);
    }

    private void ApplyTouchToSettingsMenu(Viewport vp, in DragonBreakInput input, ref bool leftHeld, ref bool rightHeld, ref bool confirm)
    {
        if (!input.Touches.TryGetBegan(out var tap) || !tap.IsTap)
            return;

        float menuScale = GetMenuScale(vp, (_settings?.Current.Ui ?? UiSettings.Default).HudScale);
        float lineH = (_hudFont?.LineSpacing ?? 18) * menuScale;

        int count = SettingsMenuItemsVisibleTopToBottom.Length; // matches DrawSettingsMenu() visible entries
        var panel = GetMenuPanelRect(vp, count, lineH);

        int row = HitTestMenuRow(vp, panel, lineH, tap.X01, tap.Y01);
        if (row < 0)
            return;

        _settingsSelectedIndex = Math.Clamp(row, 0, SettingsMenuItemsVisibleTopToBottom.Length - 1);
        SyncSettingsItemFromSelectedIndexUi();

        if (_settingsItem == SettingsItem.Apply || _settingsItem == SettingsItem.Cancel)
        {
            confirm = true;
            return;
        }

        float x = tap.X01 * vp.Width;
        if (x < panel.X + panel.Width * 0.25f)
            leftHeld = true;
        else if (x > panel.X + panel.Width * 0.75f)
            rightHeld = true;
    }

    /// <summary>
    /// Multi-touch: grab the nearest paddle on touch begin, then drag it in X/Y within bounds.
    /// Touch points are normalized [0..1].
    /// </summary>
    private void UpdateTouchDragForPaddles(in DragonBreakInput input, Viewport playfield, float dt, float minY, float maxY)
    {
        if (!input.Touches.HasAny)
        {
            ClearTouchDragState();
            return;
        }

        // Release ended touches.
        for (int i = 0; i < input.Touches.Points.Count; i++)
        {
            var touch = input.Touches.Points[i];
            if (touch.Phase == TouchPhase.Ended || touch.Phase == TouchPhase.Canceled)
            {
                _touchToPaddleIndex.Remove(touch.Id);
                _touchToPaddleOffset.Remove(touch.Id);
            }
        }

        // Capture new touches: only bind ONE touch to a paddle.
        if (_touchToPaddleIndex.Count == 0)
        {
            for (int i = 0; i < input.Touches.Points.Count; i++)
            {
                var touch = input.Touches.Points[i];
                if (touch.Phase != TouchPhase.Began)
                    continue;

                var touchPx = new Vector2(touch.X01 * playfield.Width, touch.Y01 * playfield.Height);

                int bestPaddle = -1;
                float bestDist = float.MaxValue;
                for (int p = 0; p < _paddles.Count; p++)
                {
                    var c = _paddles[p].Center;
                    float dx = touchPx.X - c.X;
                    float dy = touchPx.Y - c.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < bestDist)
                    {
                        bestDist = d2;
                        bestPaddle = p;
                    }
                }

                if (bestPaddle >= 0)
                {
                    _touchToPaddleIndex[touch.Id] = bestPaddle;
                    _touchToPaddleOffset[touch.Id] = _paddles[bestPaddle].Center - touchPx;
                    break;
                }
            }
        }

        // Apply drag for bound touch.
        foreach (var kvp in _touchToPaddleIndex)
        {
            int touchId = kvp.Key;
            int paddleIndex = kvp.Value;
            if ((uint)paddleIndex >= (uint)_paddles.Count)
                continue;

            TouchPoint? point = null;
            for (int i = 0; i < input.Touches.Points.Count; i++)
            {
                var t = input.Touches.Points[i];
                if (t.Id == touchId)
                {
                    point = t;
                    break;
                }
            }

            if (point == null)
                continue;

            var tp = point.Value;
            if (tp.Phase != TouchPhase.Began && tp.Phase != TouchPhase.Moved)
                continue;

            var touchPx = new Vector2(tp.X01 * playfield.Width, tp.Y01 * playfield.Height);
            var offset = _touchToPaddleOffset.TryGetValue(touchId, out var o) ? o : Vector2.Zero;

            var targetCenter = touchPx + offset;

            var p = _paddles[paddleIndex];
            float halfW = p.Size.X * 0.5f;

            float targetX = targetCenter.X - halfW;
            float targetY = targetCenter.Y - p.Size.Y * 0.5f;

            targetX = MathHelper.Clamp(targetX, 0f, playfield.Width - p.Size.X);
            targetY = MathHelper.Clamp(targetY, minY, maxY);

            float alpha = 1f - MathF.Exp(-TouchDragSmoothingHz * dt);
            var desiredPos = Vector2.Lerp(p.Position, new Vector2(targetX, targetY), alpha);
            var delta = desiredPos - p.Position;

            float denom = Math.Max(0.0001f, p.SpeedPixelsPerSecond * dt);
            float moveX = delta.X / denom;
            float moveY = -delta.Y / denom;

            moveX = MathHelper.Clamp(moveX, -1f, 1f);
            moveY = MathHelper.Clamp(moveY, -1f, 1f);

            p.Update(dt, moveX, moveY, playfield.Width, minY, maxY);
        }
    }
}
