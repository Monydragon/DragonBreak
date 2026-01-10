using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DragonBreak.Core.Input;

/// <summary>
/// Normalizes keyboard + gamepad input into a single device-agnostic <see cref="DragonBreakInput"/>.
/// </summary>
public sealed class InputMapper
{
    private KeyboardState _prevKeyboard;
    private GamePadState _prevGamePad;

    public DragonBreakInput Update(PlayerIndex playerIndex)
    {
        var keyboard = Keyboard.GetState();
        var pad = GamePad.GetState(playerIndex);

        float moveX = 0f;

        // Keyboard digital movement
        if (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A)) moveX -= 1f;
        if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D)) moveX += 1f;

        // GamePad analog + dpad movement
        if (pad.IsConnected)
        {
            float stickX = pad.ThumbSticks.Left.X;
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
        }

        bool serveDown = keyboard.IsKeyDown(Keys.Space) || keyboard.IsKeyDown(Keys.Enter)
                         || (pad.IsConnected && pad.Buttons.A == ButtonState.Pressed);
        bool serveWasDown = _prevKeyboard.IsKeyDown(Keys.Space) || _prevKeyboard.IsKeyDown(Keys.Enter)
                            || (_prevGamePad.IsConnected && _prevGamePad.Buttons.A == ButtonState.Pressed);

        bool pauseDown = keyboard.IsKeyDown(Keys.P)
                         || (pad.IsConnected && pad.Buttons.Start == ButtonState.Pressed);
        bool pauseWasDown = _prevKeyboard.IsKeyDown(Keys.P)
                            || (_prevGamePad.IsConnected && _prevGamePad.Buttons.Start == ButtonState.Pressed);

        bool exitDown = keyboard.IsKeyDown(Keys.Escape)
                        || (pad.IsConnected && pad.Buttons.Back == ButtonState.Pressed);
        bool exitWasDown = _prevKeyboard.IsKeyDown(Keys.Escape)
                           || (_prevGamePad.IsConnected && _prevGamePad.Buttons.Back == ButtonState.Pressed);

        // Menu navigation (also works as a secondary control scheme on desktop).
        bool menuUpDown = keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W)
                          || (pad.IsConnected && pad.DPad.Up == ButtonState.Pressed);
        bool menuUpWasDown = _prevKeyboard.IsKeyDown(Keys.Up) || _prevKeyboard.IsKeyDown(Keys.W)
                             || (_prevGamePad.IsConnected && _prevGamePad.DPad.Up == ButtonState.Pressed);

        bool menuDownDown = keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S)
                            || (pad.IsConnected && pad.DPad.Down == ButtonState.Pressed);
        bool menuDownWasDown = _prevKeyboard.IsKeyDown(Keys.Down) || _prevKeyboard.IsKeyDown(Keys.S)
                               || (_prevGamePad.IsConnected && _prevGamePad.DPad.Down == ButtonState.Pressed);

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

        // Confirm: Enter/Space/A
        bool menuConfirmDown = serveDown;
        bool menuConfirmWasDown = serveWasDown;

        // Back: Escape/B/Back
        bool menuBackDown = keyboard.IsKeyDown(Keys.Back) || keyboard.IsKeyDown(Keys.Escape)
                            || (pad.IsConnected && (pad.Buttons.B == ButtonState.Pressed || pad.Buttons.Back == ButtonState.Pressed));
        bool menuBackWasDown = _prevKeyboard.IsKeyDown(Keys.Back) || _prevKeyboard.IsKeyDown(Keys.Escape)
                               || (_prevGamePad.IsConnected && (_prevGamePad.Buttons.B == ButtonState.Pressed || _prevGamePad.Buttons.Back == ButtonState.Pressed));

        var state = new DragonBreakInput(
            moveX,
            servePressed: serveDown && !serveWasDown,
            pausePressed: pauseDown && !pauseWasDown,
            exitPressed: exitDown && !exitWasDown,
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
        _prevGamePad = pad;

        return state;
    }

    public DragonBreakInput UpdateForPlayer(PlayerIndex playerIndex)
    {
        // For now, reuse the same mapping logic with per-player gamepad state.
        // Keyboard controls are intentionally shared in local co-op.
        return Update(playerIndex);
    }
}
