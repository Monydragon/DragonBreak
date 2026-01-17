#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout.Ui;

internal sealed class PauseMenuScreen : IBreakoutScreen
{
    internal enum PauseAction
    {
        None,
        Resume,
        RestartLevel,
        HighScores,
        MainMenu,
    }

    private int _selectedIndex;
    private bool _upConsumed;
    private bool _downConsumed;

    private PauseAction _pendingAction = PauseAction.None;

    public PauseAction ConsumeAction()
    {
        var a = _pendingAction;
        _pendingAction = PauseAction.None;
        return a;
    }

    public void ResetSelection() => _selectedIndex = 0;

    public void Update(DragonBreakInput[] inputs, Viewport vp, float dtSeconds)
    {
        bool confirmPressed = false;
        bool backPressed = false;
        bool upHeldAny = false;
        bool downHeldAny = false;
        float menuY = 0f;

        for (int i = 0; i < inputs.Length; i++)
        {
            confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
            backPressed |= inputs[i].MenuBackPressed || inputs[i].PausePressed;

            upHeldAny |= inputs[i].MenuUpHeld;
            downHeldAny |= inputs[i].MenuDownHeld;

            if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(menuY))
                menuY = inputs[i].MenuMoveY;
        }

        const float deadzone = 0.55f;
        bool upHeld = upHeldAny || menuY >= deadzone;
        bool downHeld = downHeldAny || menuY <= -deadzone;

        const int itemCount = 4;

        if (upHeld && !_upConsumed)
        {
            _selectedIndex = (_selectedIndex - 1 + itemCount) % itemCount;
            _upConsumed = true;
        }
        if (!upHeld) _upConsumed = false;

        if (downHeld && !_downConsumed)
        {
            _selectedIndex = (_selectedIndex + 1) % itemCount;
            _downConsumed = true;
        }
        if (!downHeld) _downConsumed = false;

        if (backPressed)
        {
            _pendingAction = PauseAction.Resume;
            return;
        }

        if (confirmPressed)
        {
            _pendingAction = _selectedIndex switch
            {
                0 => PauseAction.Resume,
                1 => PauseAction.RestartLevel,
                2 => PauseAction.HighScores,
                3 => PauseAction.MainMenu,
                _ => PauseAction.Resume,
            };
        }
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // BreakoutWorld owns drawing (font/pixel). This screen only tracks selection state.
    }

    public IEnumerable<(string Label, bool Selected)> GetLines()
    {
        yield return ("Resume", _selectedIndex == 0);
        yield return ("Restart Level", _selectedIndex == 1);
        yield return ("High Scores", _selectedIndex == 2);
        yield return ("Main Menu", _selectedIndex == 3);
    }
}
