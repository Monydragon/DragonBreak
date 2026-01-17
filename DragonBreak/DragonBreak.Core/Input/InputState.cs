using Microsoft.Xna.Framework;

namespace DragonBreak.Core.Input;

public readonly struct DragonBreakInput
{
    public float MoveX { get; }
    public float MoveY { get; }
    public bool ServePressed { get; }
    public bool PausePressed { get; }
    public bool ExitPressed { get; }

    // Space-only catch/launch control (press to arm catch; release to launch).
    public bool CatchHeld { get; }
    public bool CatchPressed { get; }
    public bool CatchReleased { get; }

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
        float moveY,
        bool servePressed,
        bool pausePressed,
        bool exitPressed,
        bool catchHeld = false,
        bool catchPressed = false,
        bool catchReleased = false,
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
        MoveY = MathHelper.Clamp(moveY, -1f, 1f);
        ServePressed = servePressed;
        PausePressed = pausePressed;
        ExitPressed = exitPressed;

        CatchHeld = catchHeld;
        CatchPressed = catchPressed;
        CatchReleased = catchReleased;

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
