using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DragonBreak.Core.Input;

/// <summary>
/// Normalizes keyboard + gamepad input into a single device-agnostic <see cref="DragonBreakInput"/>.
/// </summary>
public sealed class InputMapper
{
    private KeyboardState _prevKeyboard;

    // Track previous pad state PER player; using one shared GamePadState breaks pressed/released detection
    // when UpdateForPlayer is called for multiple PlayerIndex values in the same frame.
    private readonly GamePadState[] _prevGamePadByPlayer = new GamePadState[4];

    public DragonBreakInput Update(PlayerIndex playerIndex)
    {
        var keyboard = Keyboard.GetState();
        var pad = GamePad.GetState(playerIndex);

        int pIndex = (int)playerIndex;
        if ((uint)pIndex >= (uint)_prevGamePadByPlayer.Length)
            pIndex = 0;

        var prevPad = _prevGamePadByPlayer[pIndex];

        float moveX = 0f;
        float moveY = 0f;

        // Keyboard digital movement
        if (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A)) moveX -= 1f;
        if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D)) moveX += 1f;
        if (keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W)) moveY += 1f;
        if (keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S)) moveY -= 1f;

        // GamePad analog + dpad movement
        if (pad.IsConnected)
        {
            float stickX = pad.ThumbSticks.Left.X;
            float stickY = pad.ThumbSticks.Left.Y;

            // ThumbSticks are already in [-1..1].
            if (System.Math.Abs(stickX) > 0.2f)
            {
                moveX = stickX;
            }
            else
            {
                if (pad.DPad.Left == ButtonState.Pressed) moveX -= 1f;
                if (pad.DPad.Right == ButtonState.Pressed) moveX += 1f;
            }

            if (System.Math.Abs(stickY) > 0.2f)
            {
                moveY = stickY;
            }
            else
            {
                if (pad.DPad.Up == ButtonState.Pressed) moveY += 1f;
                if (pad.DPad.Down == ButtonState.Pressed) moveY -= 1f;
            }
        }

        // Serve (start ball) control.
        // Keyboard: Enter
        // Gamepad: A (Xbox) / Cross (PlayStation) OR Y (Triangle) as alternate.
        // Note: A is also used for catch hold/release; serving uses a pressed event, so holding A won't auto-serve.
        bool serveDown = keyboard.IsKeyDown(Keys.Enter)
                         || (pad.IsConnected && (pad.Buttons.A == ButtonState.Pressed || pad.Buttons.Y == ButtonState.Pressed));
        bool serveWasDown = _prevKeyboard.IsKeyDown(Keys.Enter)
                            || (prevPad.IsConnected && (prevPad.Buttons.A == ButtonState.Pressed || prevPad.Buttons.Y == ButtonState.Pressed));

        // Catch / launch control.
        // Keyboard: Space.
        // Gamepad: "south" button (A on Xbox, Cross on PlayStation when mapped through XInput).
        bool catchDown = keyboard.IsKeyDown(Keys.Space)
                         || (pad.IsConnected && pad.Buttons.A == ButtonState.Pressed);
        bool catchWasDown = _prevKeyboard.IsKeyDown(Keys.Space)
                            || (prevPad.IsConnected && prevPad.Buttons.A == ButtonState.Pressed);
        bool catchPressed = catchDown && !catchWasDown;
        bool catchReleased = !catchDown && catchWasDown;

        bool pauseDown = keyboard.IsKeyDown(Keys.P)
                         || (pad.IsConnected && pad.Buttons.Start == ButtonState.Pressed);
        bool pauseWasDown = _prevKeyboard.IsKeyDown(Keys.P)
                            || (prevPad.IsConnected && prevPad.Buttons.Start == ButtonState.Pressed);

        bool exitDown = keyboard.IsKeyDown(Keys.Escape)
                        || (pad.IsConnected && pad.Buttons.Back == ButtonState.Pressed);
        bool exitWasDown = _prevKeyboard.IsKeyDown(Keys.Escape)
                           || (prevPad.IsConnected && prevPad.Buttons.Back == ButtonState.Pressed);

        // Menu navigation (also works as a secondary control scheme on desktop).
        bool menuUpDown = keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W)
                          || (pad.IsConnected && pad.DPad.Up == ButtonState.Pressed);
        bool menuUpWasDown = _prevKeyboard.IsKeyDown(Keys.Up) || _prevKeyboard.IsKeyDown(Keys.W)
                             || (prevPad.IsConnected && prevPad.DPad.Up == ButtonState.Pressed);

        bool menuDownDown = keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S)
                            || (pad.IsConnected && pad.DPad.Down == ButtonState.Pressed);
        bool menuDownWasDown = _prevKeyboard.IsKeyDown(Keys.Down) || _prevKeyboard.IsKeyDown(Keys.S)
                               || (prevPad.IsConnected && prevPad.DPad.Down == ButtonState.Pressed);

        // Left/right in menu: arrow keys / A-D / dpad / stick
        bool menuLeftHeld = keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A)
                            || (pad.IsConnected && (pad.DPad.Left == ButtonState.Pressed || pad.ThumbSticks.Left.X <= -0.4f));
        bool menuRightHeld = keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D)
                             || (pad.IsConnected && (pad.DPad.Right == ButtonState.Pressed || pad.ThumbSticks.Left.X >= 0.4f));

        // Provide analog menu axes so we can do one-step-per-flick behavior.
        float menuMoveX = 0f;
        float menuMoveY = 0f;

        if (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A)) menuMoveX -= 1f;
        if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D)) menuMoveX += 1f;
        if (keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W)) menuMoveY += 1f;
        if (keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S)) menuMoveY -= 1f;

        if (pad.IsConnected)
        {
            // DPad overrides stick if pressed.
            if (pad.DPad.Left == ButtonState.Pressed) menuMoveX = -1f;
            else if (pad.DPad.Right == ButtonState.Pressed) menuMoveX = 1f;
            else menuMoveX = pad.ThumbSticks.Left.X;

            if (pad.DPad.Up == ButtonState.Pressed) menuMoveY = 1f;
            else if (pad.DPad.Down == ButtonState.Pressed) menuMoveY = -1f;
            else menuMoveY = pad.ThumbSticks.Left.Y;
        }

        // Confirm: Enter/Space/A (keep this independent of ServePressed so menu navigation doesn't regress).
        bool menuConfirmDown = keyboard.IsKeyDown(Keys.Enter)
                               || keyboard.IsKeyDown(Keys.Space)
                               || (pad.IsConnected && pad.Buttons.A == ButtonState.Pressed);
        bool menuConfirmWasDown = _prevKeyboard.IsKeyDown(Keys.Enter)
                                  || _prevKeyboard.IsKeyDown(Keys.Space)
                                  || (prevPad.IsConnected && prevPad.Buttons.A == ButtonState.Pressed);

        // Back: Escape/B/Back
        bool menuBackDown = keyboard.IsKeyDown(Keys.Back) || keyboard.IsKeyDown(Keys.Escape)
                            || (pad.IsConnected && (pad.Buttons.B == ButtonState.Pressed || pad.Buttons.Back == ButtonState.Pressed));
        bool menuBackWasDown = _prevKeyboard.IsKeyDown(Keys.Back) || _prevKeyboard.IsKeyDown(Keys.Escape)
                               || (prevPad.IsConnected && (prevPad.Buttons.B == ButtonState.Pressed || prevPad.Buttons.Back == ButtonState.Pressed));

        var state = new DragonBreakInput(
            moveX,
            moveY,
            servePressed: serveDown && !serveWasDown,
            pausePressed: pauseDown && !pauseWasDown,
            exitPressed: exitDown && !exitWasDown,
            catchHeld: catchDown,
            catchPressed: catchPressed,
            catchReleased: catchReleased,
            menuUpPressed: menuUpDown && !menuUpWasDown,
            menuDownPressed: menuDownDown && !menuDownWasDown,
            menuConfirmPressed: menuConfirmDown && !menuConfirmWasDown,
            menuBackPressed: menuBackDown && !menuBackWasDown,
            menuUpHeld: menuUpDown,
            menuDownHeld: menuDownDown,
            menuLeftHeld: menuLeftHeld,
            menuRightHeld: menuRightHeld,
            menuMoveX: menuMoveX,
            menuMoveY: menuMoveY);

        _prevKeyboard = keyboard;
        _prevGamePadByPlayer[pIndex] = pad;

        return state;
    }

    public DragonBreakInput UpdateForPlayer(PlayerIndex playerIndex)
    {
        // For now, reuse the same mapping logic with per-player gamepad state.
        // Keyboard controls are intentionally shared in local co-op.
        return Update(playerIndex);
    }
}
