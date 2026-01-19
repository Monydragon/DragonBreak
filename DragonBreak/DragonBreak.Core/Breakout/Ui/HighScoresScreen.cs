#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Highscores;
using DragonBreak.Core.Input;
using DragonBreak.Core.Settings;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout.Ui;

internal sealed class HighScoresScreen : IBreakoutScreen
{
    internal enum HighScoresAction
    {
        None,
        Back,
    }

    private readonly HighScoreService _service;

    private GameModeId _mode;
    private DifficultyId _difficulty;

    private int _selectedIndex;
    private int _scrollOffset;

    // Input consumption
    private bool _upConsumed;
    private bool _downConsumed;
    private bool _pageUpConsumed;
    private bool _pageDownConsumed;

    private HighScoresAction _pendingAction;

    public HighScoresScreen(HighScoreService service)
    {
        _service = service;
    }

    public void Show(GameModeId mode, DifficultyId difficulty)
    {
        _mode = mode;
        _difficulty = difficulty;
        _selectedIndex = 0;
        _scrollOffset = 0;
        _pendingAction = HighScoresAction.None;
    }

    public HighScoresAction ConsumeAction()
    {
        var a = _pendingAction;
        _pendingAction = HighScoresAction.None;
        return a;
    }

    public void Update(DragonBreakInput[] inputs, Viewport vp, float dtSeconds)
    {
        // Defensive: some callers may pass an empty input array.
        if (inputs.Length == 0)
            inputs = Array.Empty<DragonBreakInput>();

        bool backPressed = false;
        bool upHeldAny = false;
        bool downHeldAny = false;
        float menuY = 0f;

        bool pageUpHeld = false;
        bool pageDownHeld = false;
        float menuX = 0f;

        for (int i = 0; i < inputs.Length; i++)
        {
            backPressed |= inputs[i].MenuBackPressed || inputs[i].PausePressed;
            upHeldAny |= inputs[i].MenuUpHeld;
            downHeldAny |= inputs[i].MenuDownHeld;

            // Page navigation: shoulder buttons mapped to MenuLeftHeld/MenuRightHeld.
            // Also allow analog horizontal (MenuMoveX) as a fallback on controllers.
            pageUpHeld |= inputs[i].MenuLeftHeld;
            pageDownHeld |= inputs[i].MenuRightHeld;

            if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(menuY))
                menuY = inputs[i].MenuMoveY;

            if (Math.Abs(inputs[i].MenuMoveX) > Math.Abs(menuX))
                menuX = inputs[i].MenuMoveX;
        }

        const float deadzone = 0.55f;
        bool upHeld = upHeldAny || menuY >= deadzone;
        bool downHeld = downHeldAny || menuY <= -deadzone;

        bool pageUpPressed = pageUpHeld || menuX <= -deadzone;
        bool pageDownPressed = pageDownHeld || menuX >= deadzone;

        var scores = _service.GetTop(_mode, _difficulty);
        int count = scores.Count;

        // Visible lines: keep some margin for title and footer.
        int visible = Math.Max(3, (vp.Height - 220) / 28);

        // Clamp selection and scroll safely even if the score list size changed.
        if (count <= 0)
        {
            _selectedIndex = 0;
            _scrollOffset = 0;
        }
        else
        {
            _selectedIndex = Math.Clamp(_selectedIndex, 0, count - 1);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, count - visible));
        }

        if (upHeld && !_upConsumed)
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            _upConsumed = true;
        }
        if (!upHeld) _upConsumed = false;

        if (downHeld && !_downConsumed)
        {
            _selectedIndex = Math.Min(Math.Max(0, count - 1), _selectedIndex + 1);
            _downConsumed = true;
        }
        if (!downHeld) _downConsumed = false;

        if (pageUpPressed && !_pageUpConsumed)
        {
            _selectedIndex = Math.Max(0, _selectedIndex - visible);
            _pageUpConsumed = true;
        }
        if (!pageUpPressed) _pageUpConsumed = false;

        if (pageDownPressed && !_pageDownConsumed)
        {
            _selectedIndex = Math.Min(Math.Max(0, count - 1), _selectedIndex + visible);
            _pageDownConsumed = true;
        }
        if (!pageDownPressed) _pageDownConsumed = false;

        // Keep selection in view.
        if (count > 0)
        {
            // Re-clamp after applying navigation.
            _selectedIndex = Math.Clamp(_selectedIndex, 0, count - 1);

            if (_selectedIndex < _scrollOffset)
                _scrollOffset = _selectedIndex;
            if (_selectedIndex >= _scrollOffset + visible)
                _scrollOffset = _selectedIndex - visible + 1;

            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, count - visible));
        }
        else
        {
            _selectedIndex = 0;
            _scrollOffset = 0;
        }

        if (backPressed)
            _pendingAction = HighScoresAction.Back;
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // Drawn by BreakoutWorld (font/pixel). This screen is state-only.
    }

    public IEnumerable<(string Text, bool Selected)> GetLines(Viewport vp)
    {
        string title = $"HIGH SCORES";
        string ctx = $"MODE: {_mode}   DIFF: {_difficulty}";

        yield return (title, false);
        yield return (ctx, false);
        yield return ("", false);

        var scores = _service.GetTop(_mode, _difficulty);
        if (scores.Count == 0)
        {
            yield return ("No scores yet.", false);
            yield break;
        }

        int visible = Math.Max(3, (vp.Height - 220) / 28);

        // Defensive clamp in case Update wasn't called yet or score list changed between frames.
        int count = scores.Count;
        int sel = Math.Clamp(_selectedIndex, 0, count - 1);
        int start = Math.Clamp(_scrollOffset, 0, Math.Max(0, count - visible));
        int end = Math.Min(count, start + visible);

        for (int i = start; i < end; i++)
        {
            var e = scores[i];
            string name = string.IsNullOrWhiteSpace(e.Name) ? "PLAYER" : e.Name;
            string playersSuffix = e.PlayerCount > 1 ? $" ({e.PlayerCount}P)" : "";
            string line = $"{i + 1,2}. {name,-12}  {e.Score,7}  L{e.LevelReached + 1}{playersSuffix}";
            yield return (line, i == sel);
        }

        yield return ("", false);
        yield return ("Up/Down or stick to scroll", false);
        yield return ("L/R shoulder (or left/right) = page", false);
        yield return ("Back = close", false);
    }
}
