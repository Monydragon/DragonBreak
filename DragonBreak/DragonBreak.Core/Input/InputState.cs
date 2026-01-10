using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Input;

public readonly struct DragonBreakInput
{
    public float MoveX { get; }
    public bool ServePressed { get; }
    public bool PausePressed { get; }
    public bool ExitPressed { get; }

    public bool MenuUpPressed { get; }
    public bool MenuDownPressed { get; }
    public bool MenuConfirmPressed { get; }
    public bool MenuBackPressed { get; }

    public bool MenuUpHeld { get; }
    public bool MenuDownHeld { get; }

    public bool MenuLeftHeld { get; }
    public bool MenuRightHeld { get; }

    public float MenuMoveX { get; }
    public float MenuMoveY { get; }

    public DragonBreakInput(
        float moveX,
        bool servePressed,
        bool pausePressed,
        bool exitPressed,
        bool menuUpPressed = false,
        bool menuDownPressed = false,
        bool menuConfirmPressed = false,
        bool menuBackPressed = false,
        bool menuUpHeld = false,
        bool menuDownHeld = false,
        bool menuLeftHeld = false,
        bool menuRightHeld = false,
        float menuMoveX = 0f,
        float menuMoveY = 0f)
    {
        MoveX = MathHelper.Clamp(moveX, -1f, 1f);
        ServePressed = servePressed;
        PausePressed = pausePressed;
        ExitPressed = exitPressed;

        MenuUpPressed = menuUpPressed;
        MenuDownPressed = menuDownPressed;
        MenuConfirmPressed = menuConfirmPressed;
        MenuBackPressed = menuBackPressed;

        MenuUpHeld = menuUpHeld;
        MenuDownHeld = menuDownHeld;
        MenuLeftHeld = menuLeftHeld;
        MenuRightHeld = menuRightHeld;

        MenuMoveX = MathHelper.Clamp(menuMoveX, -1f, 1f);
        MenuMoveY = MathHelper.Clamp(menuMoveY, -1f, 1f);
    }
}
