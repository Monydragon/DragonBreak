#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Highscores;
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout.Ui;

internal sealed class GameOverScreen : IBreakoutScreen
{
    internal enum GameOverAction
    {
        None,
        Retry,
        MainMenu,
    }

    private readonly HighScoreService _highscores;

    private GameModeId _mode;
    private Settings.DifficultyId _difficulty;

    private int _finalScore;
    private int _levelReached;
    private int _seed;

    private bool _showSavedMessage;

    private int _selectedAction;

    private bool _leftConsumed;
    private bool _rightConsumed;
    private bool _upConsumed;
    private bool _downConsumed;

    private bool _incConsumed;
    private bool _decConsumed;

    private GameOverAction _pending;

    public GameOverScreen(HighScoreService highscores)
    {
        _highscores = highscores;
    }

    public void Show(int finalScore, GameModeId mode, Settings.DifficultyId difficulty, int levelReached, int seed, bool showSavedMessage)
    {
        _finalScore = Math.Max(0, finalScore);
        _mode = mode;
        _difficulty = difficulty;
        _levelReached = Math.Max(0, levelReached);
        _seed = seed;

        _showSavedMessage = showSavedMessage;

        _pending = GameOverAction.None;
        _selectedAction = 0;

        // Reset input consumption.
        _upConsumed = _downConsumed = _leftConsumed = _rightConsumed = false;
        _incConsumed = _decConsumed = false;
    }

    public GameOverAction ConsumeAction()
    {
        var a = _pending;
        _pending = GameOverAction.None;
        return a;
    }

    public void Update(DragonBreakInput[] inputs, Viewport vp, float dtSeconds)
    {
        bool confirmPressed = false;
        bool backPressed = false;

        bool upHeldAny = false;
        bool downHeldAny = false;
        float my = 0f;

        for (int i = 0; i < inputs.Length; i++)
        {
            confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
            backPressed |= inputs[i].MenuBackPressed;

            upHeldAny |= inputs[i].MenuUpHeld;
            downHeldAny |= inputs[i].MenuDownHeld;

            if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(my)) my = inputs[i].MenuMoveY;
        }

        const float deadzone = 0.55f;
        bool upHeld = upHeldAny || my >= deadzone;
        bool downHeld = downHeldAny || my <= -deadzone;

        int actionCount = 2;

        if (upHeld && !_upConsumed)
        {
            _selectedAction = (_selectedAction - 1 + actionCount) % actionCount;
            _upConsumed = true;
        }
        if (!upHeld) _upConsumed = false;

        if (downHeld && !_downConsumed)
        {
            _selectedAction = (_selectedAction + 1) % actionCount;
            _downConsumed = true;
        }
        if (!downHeld) _downConsumed = false;

        if (backPressed)
        {
            _pending = GameOverAction.MainMenu;
            return;
        }

        if (confirmPressed)
        {
            if (_selectedAction == 0)
                _pending = GameOverAction.Retry;
            else
                _pending = GameOverAction.MainMenu;
        }
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // Drawn by BreakoutWorld.
    }

    public IEnumerable<(string Text, bool Selected)> GetLines(Viewport vp)
    {
        yield return ("GAME OVER", false);
        yield return ($"SCORE: {_finalScore}", false);
        yield return ($"MODE: {_mode}  DIFF: {_difficulty}  LEVEL: {_levelReached + 1}", false);
        yield return ("", false);

        if (_showSavedMessage)
        {
            yield return ("Saved to local highscores!", false);
            yield return ("", false);
        }

        // Show top scores preview.
        var top = _highscores.GetTop(_mode, _difficulty);
        yield return ("TOP LOCAL:", false);
        if (top.Count == 0)
        {
            yield return ("(none yet)", false);
        }
        else
        {
            int preview = Math.Min(5, top.Count);
            for (int i = 0; i < preview; i++)
            {
                var e = top[i];
                string nm = string.IsNullOrWhiteSpace(e.Name) ? "PLAYER" : e.Name;
                yield return ($"{i + 1,2}. {nm,-12}  {e.Score,7}", false);
            }
        }

        yield return ("", false);

        yield return ((_selectedAction == 0 ? "> Retry" : "  Retry"), _selectedAction == 0);
        yield return ((_selectedAction == 1 ? "> Main Menu" : "  Main Menu"), _selectedAction == 1);
    }
}

