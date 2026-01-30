#nullable enable
using System;
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout.Ui;

internal sealed class DebugJumpLevelScreen : IBreakoutScreen
{
    internal enum JumpAction
    {
        None,
        Confirm,
        Cancel,
    }

    private int _level = 1; // 1-based for UI
    private JumpAction _pendingAction = JumpAction.None;

    private bool _leftConsumed;
    private bool _rightConsumed;
    private bool _upConsumed;
    private bool _downConsumed;

    public int Level
    {
        get => _level;
        set => _level = Math.Clamp(value, 1, 9999);
    }

    public JumpAction ConsumeAction()
    {
        var a = _pendingAction;
        _pendingAction = JumpAction.None;
        return a;
    }

    public void Update(DragonBreakInput[] inputs, Viewport vp, float dtSeconds)
    {
        bool confirmPressed = false;
        bool backPressed = false;

        bool leftHeldAny = false;
        bool rightHeldAny = false;
        bool upHeldAny = false;
        bool downHeldAny = false;

        float menuX = 0f;
        float menuY = 0f;

        for (int i = 0; i < inputs.Length; i++)
        {
            confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
            backPressed |= inputs[i].MenuBackPressed || inputs[i].PausePressed;

            leftHeldAny |= inputs[i].MenuLeftHeld;
            rightHeldAny |= inputs[i].MenuRightHeld;
            upHeldAny |= inputs[i].MenuUpHeld;
            downHeldAny |= inputs[i].MenuDownHeld;

            if (Math.Abs(inputs[i].MenuMoveX) > Math.Abs(menuX)) menuX = inputs[i].MenuMoveX;
            if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(menuY)) menuY = inputs[i].MenuMoveY;
        }

        const float deadzone = 0.55f;
        bool leftHeld = leftHeldAny || menuX <= -deadzone;
        bool rightHeld = rightHeldAny || menuX >= deadzone;
        bool upHeld = upHeldAny || menuY >= deadzone;
        bool downHeld = downHeldAny || menuY <= -deadzone;

        // Small adjustments (dpad/left stick left/right)
        if (leftHeld && !_leftConsumed)
        {
            Level -= 1;
            _leftConsumed = true;
        }
        if (!leftHeld) _leftConsumed = false;

        if (rightHeld && !_rightConsumed)
        {
            Level += 1;
            _rightConsumed = true;
        }
        if (!rightHeld) _rightConsumed = false;

        // Larger adjustments (up/down)
        if (upHeld && !_upConsumed)
        {
            Level += 10;
            _upConsumed = true;
        }
        if (!upHeld) _upConsumed = false;

        if (downHeld && !_downConsumed)
        {
            Level -= 10;
            _downConsumed = true;
        }
        if (!downHeld) _downConsumed = false;

        // Numeric entry via keyboard is handled by BreakoutWorld (TextInput/KeyboardState).

        if (backPressed)
        {
            _pendingAction = JumpAction.Cancel;
            return;
        }

        if (confirmPressed)
        {
            _pendingAction = JumpAction.Confirm;
        }
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // BreakoutWorld owns drawing (font/pixel). This screen only tracks state.
    }

    public (string Line1, string Line2, string Line3) GetPromptLines()
    {
        return (
            $"[Debug] Jump to Level: {Level}",
            "Left/Right: +/-1   Up/Down: +/-10",
            "Confirm: A/Enter    Back: B/Esc"
        );
    }
}

