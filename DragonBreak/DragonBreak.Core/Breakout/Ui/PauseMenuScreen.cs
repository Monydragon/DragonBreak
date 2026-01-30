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

        // Debug-only
        DebugCompleteLevel,
        DebugRestartGame,
    }

    private int _selectedIndex;
    private bool _upConsumed;
    private bool _downConsumed;

    private PauseAction _pendingAction = PauseAction.None;

    private bool _debugEnabled;

    public void SetDebugEnabled(bool enabled)
    {
        _debugEnabled = enabled;

        // Keep selection valid when item count changes.
        int count = GetItems().Count;
        if (count <= 0)
            _selectedIndex = 0;
        else
            _selectedIndex = Math.Clamp(_selectedIndex, 0, count - 1);
    }

    private List<(string Label, PauseAction Action)> GetItems()
    {
        var items = new List<(string Label, PauseAction Action)>(8)
        {
            ("Resume", PauseAction.Resume),
            ("Restart Level", PauseAction.RestartLevel),
            ("High Scores", PauseAction.HighScores),
            ("Main Menu", PauseAction.MainMenu),
        };

        if (_debugEnabled)
        {
            items.Add(("[Debug] Complete Level", PauseAction.DebugCompleteLevel));
            items.Add(("[Debug] Restart Game", PauseAction.DebugRestartGame));
        }

        return items;
    }

    public int GetItemCount() => GetItems().Count;

    public PauseAction GetActionForItemIndex(int itemIndex)
    {
        var items = GetItems();
        if ((uint)itemIndex >= (uint)items.Count)
            return PauseAction.None;
        return items[itemIndex].Action;
    }

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

        int itemCount = GetItemCount();
        if (itemCount <= 0)
            itemCount = 1;

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
            _pendingAction = GetActionForItemIndex(_selectedIndex);
            if (_pendingAction == PauseAction.None)
                _pendingAction = PauseAction.Resume;
        }
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // BreakoutWorld owns drawing (font/pixel). This screen only tracks selection state.
    }

    public IEnumerable<(string Label, bool Selected)> GetLines()
    {
        var items = GetItems();
        for (int i = 0; i < items.Count; i++)
            yield return (items[i].Label, _selectedIndex == i);
    }
}
