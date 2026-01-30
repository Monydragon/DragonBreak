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

    private static Keys GetKeyMoveLeft(int playerIndex) => playerIndex switch
    {
        1 => Keys.Left,
        2 => Keys.J,
        3 => Keys.NumPad4,
        _ => Keys.A,
    };

    private static Keys GetKeyMoveRight(int playerIndex) => playerIndex switch
    {
        1 => Keys.Right,
        2 => Keys.L,
        3 => Keys.NumPad6,
        _ => Keys.D,
    };

    private static Keys GetKeyMoveUp(int playerIndex) => playerIndex switch
    {
        1 => Keys.Up,
        2 => Keys.I,
        3 => Keys.NumPad8,
        _ => Keys.W,
    };

    private static Keys GetKeyMoveDown(int playerIndex) => playerIndex switch
    {
        1 => Keys.Down,
        2 => Keys.K,
        3 => Keys.NumPad5,
        _ => Keys.S,
    };

    // Keep global UI keys so any player can navigate menus.
    private static bool KeyboardMenuUp(KeyboardState k)
        => k.IsKeyDown(Keys.Up) || k.IsKeyDown(Keys.W) || k.IsKeyDown(Keys.I);

    private static bool KeyboardMenuDown(KeyboardState k)
        => k.IsKeyDown(Keys.Down) || k.IsKeyDown(Keys.S) || k.IsKeyDown(Keys.K);

    private static bool KeyboardMenuLeft(KeyboardState k)
        => k.IsKeyDown(Keys.Left) || k.IsKeyDown(Keys.A) || k.IsKeyDown(Keys.J);

    private static bool KeyboardMenuRight(KeyboardState k)
        => k.IsKeyDown(Keys.Right) || k.IsKeyDown(Keys.D) || k.IsKeyDown(Keys.L);

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

        // Keyboard digital movement (per-player schemes)
        Keys kLeft = GetKeyMoveLeft(pIndex);
        Keys kRight = GetKeyMoveRight(pIndex);
        Keys kUp = GetKeyMoveUp(pIndex);
        Keys kDown = GetKeyMoveDown(pIndex);

        if (keyboard.IsKeyDown(kLeft)) moveX -= 1f;
        if (keyboard.IsKeyDown(kRight)) moveX += 1f;
        if (keyboard.IsKeyDown(kUp)) moveY += 1f;
        if (keyboard.IsKeyDown(kDown)) moveY -= 1f;

        // GamePad analog + dpad movement (combined with keyboard: pad overrides if meaningful input)
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
                if (pad.DPad.Left == ButtonState.Pressed) moveX = -1f;
                else if (pad.DPad.Right == ButtonState.Pressed) moveX = 1f;
            }

            if (System.Math.Abs(stickY) > 0.2f)
            {
                moveY = stickY;
            }
            else
            {
                if (pad.DPad.Up == ButtonState.Pressed) moveY = 1f;
                else if (pad.DPad.Down == ButtonState.Pressed) moveY = -1f;
            }
        }

        // Serve (start ball) control.
        // Keyboard: Enter for Player 1, RightControl for Player 2, RightShift for Player 3, NumPad0 for Player 4.
        // Gamepad: A (Xbox) / Cross (PlayStation) OR Y (Triangle) as alternate.
        // Note: A is also used for catch hold/release; serving uses a pressed event, so holding A won't auto-serve.
        Keys serveKey = pIndex switch
        {
            1 => Keys.RightControl,
            2 => Keys.RightShift,
            3 => Keys.NumPad0,
            _ => Keys.Enter,
        };

        bool serveDown = keyboard.IsKeyDown(serveKey)
                         || (pad.IsConnected && (pad.Buttons.A == ButtonState.Pressed || pad.Buttons.Y == ButtonState.Pressed));
        bool serveWasDown = _prevKeyboard.IsKeyDown(serveKey)
                            || (prevPad.IsConnected && (prevPad.Buttons.A == ButtonState.Pressed || prevPad.Buttons.Y == ButtonState.Pressed));

        // Catch / launch control.
        // Keyboard: Space for Player 1, RightAlt for Player 2, LeftShift for Player 3, NumPad1 for Player 4.
        // Gamepad: "south" button (A on Xbox, Cross on PlayStation when mapped through XInput).
        Keys catchKey = pIndex switch
        {
            1 => Keys.RightAlt,
            2 => Keys.LeftShift,
            3 => Keys.NumPad1,
            _ => Keys.Space,
        };

        bool catchDown = keyboard.IsKeyDown(catchKey)
                         || (pad.IsConnected && pad.Buttons.A == ButtonState.Pressed);
        bool catchWasDown = _prevKeyboard.IsKeyDown(catchKey)
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

        // Menu navigation (keyboard is shared; allow arrows/WASD/IJKL)
        // IMPORTANT: menuMoveY is defined as +1 = Up, -1 = Down.
        // Keep MenuUpHeld/MenuDownHeld consistent with that.
        bool menuUpDown = KeyboardMenuUp(keyboard)
                          || (pad.IsConnected && pad.DPad.Up == ButtonState.Pressed);
        bool menuUpWasDown = KeyboardMenuUp(_prevKeyboard)
                             || (prevPad.IsConnected && prevPad.DPad.Up == ButtonState.Pressed);

        bool menuDownDown = KeyboardMenuDown(keyboard)
                            || (pad.IsConnected && pad.DPad.Down == ButtonState.Pressed);
        bool menuDownWasDown = KeyboardMenuDown(_prevKeyboard)
                               || (prevPad.IsConnected && prevPad.DPad.Down == ButtonState.Pressed);

        // Left/right in menu: arrow keys / A-D / J-L / dpad / stick
        bool menuLeftHeld = KeyboardMenuLeft(keyboard)
                            || (pad.IsConnected && (pad.DPad.Left == ButtonState.Pressed || pad.ThumbSticks.Left.X <= -0.4f));
        bool menuRightHeld = KeyboardMenuRight(keyboard)
                             || (pad.IsConnected && (pad.DPad.Right == ButtonState.Pressed || pad.ThumbSticks.Left.X >= 0.4f));

        // Provide analog menu axes so we can do one-step-per-flick behavior.
        float menuMoveX = 0f;
        float menuMoveY = 0f;

        if (KeyboardMenuLeft(keyboard)) menuMoveX = -1f;
        else if (KeyboardMenuRight(keyboard)) menuMoveX = 1f;

        if (KeyboardMenuUp(keyboard)) menuMoveY = 1f;
        else if (KeyboardMenuDown(keyboard)) menuMoveY = -1f;

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

        // Back: Escape/B/Back (NOT Backspace; Backspace is used for text editing in name entry)
        bool menuBackDown = keyboard.IsKeyDown(Keys.Escape)
                            || (pad.IsConnected && (pad.Buttons.B == ButtonState.Pressed || pad.Buttons.Back == ButtonState.Pressed));
        bool menuBackWasDown = _prevKeyboard.IsKeyDown(Keys.Escape)
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
