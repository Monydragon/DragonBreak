#nullable enable
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

    // Optional absolute pointer (touch/mouse) target in normalized [0..1] screen coordinates.
    public bool PointerActive { get; }
    public float PointerX01 { get; }
    public float PointerY01 { get; }

    // Multitouch support (primarily for mobile). This is separate from the legacy single-pointer fields.
    public TouchState Touches { get; }

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
        float menuMoveY = 0f,
        bool pointerActive = false,
        float pointerX01 = 0f,
        float pointerY01 = 0f,
        TouchState? touches = null)
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

        PointerActive = pointerActive;
        PointerX01 = MathHelper.Clamp(pointerX01, 0f, 1f);
        PointerY01 = MathHelper.Clamp(pointerY01, 0f, 1f);

        Touches = touches ?? TouchState.Empty;
    }

    public DragonBreakInput WithPointer(TouchPointerState pointer)
        => new(
            moveX: MoveX,
            moveY: MoveY,
            servePressed: ServePressed,
            pausePressed: PausePressed,
            exitPressed: ExitPressed,
            catchHeld: CatchHeld,
            catchPressed: CatchPressed,
            catchReleased: CatchReleased,
            menuUpPressed: MenuUpPressed,
            menuDownPressed: MenuDownPressed,
            menuConfirmPressed: MenuConfirmPressed,
            menuBackPressed: MenuBackPressed,
            menuUpHeld: MenuUpHeld,
            menuDownHeld: MenuDownHeld,
            menuLeftHeld: MenuLeftHeld,
            menuRightHeld: MenuRightHeld,
            menuMoveX: MenuMoveX,
            menuMoveY: MenuMoveY,
            pointerActive: pointer.IsActive,
            pointerX01: pointer.X01,
            pointerY01: pointer.Y01,
            touches: Touches);

    public DragonBreakInput WithTouches(TouchState touches)
    {
        // For compatibility with older code that expects PointerActive, also map the first active touch.
        bool pointerActive = false;
        float x01 = PointerX01;
        float y01 = PointerY01;

        if (touches != null && touches.TryGetAnyActive(out var first))
        {
            pointerActive = true;
            x01 = first.X01;
            y01 = first.Y01;
        }

        return new DragonBreakInput(
            moveX: MoveX,
            moveY: MoveY,
            servePressed: ServePressed,
            pausePressed: PausePressed,
            exitPressed: ExitPressed,
            catchHeld: CatchHeld,
            catchPressed: CatchPressed,
            catchReleased: CatchReleased,
            menuUpPressed: MenuUpPressed,
            menuDownPressed: MenuDownPressed,
            menuConfirmPressed: MenuConfirmPressed,
            menuBackPressed: MenuBackPressed,
            menuUpHeld: MenuUpHeld,
            menuDownHeld: MenuDownHeld,
            menuLeftHeld: MenuLeftHeld,
            menuRightHeld: MenuRightHeld,
            menuMoveX: MenuMoveX,
            menuMoveY: MenuMoveY,
            pointerActive: pointerActive,
            pointerX01: x01,
            pointerY01: y01,
            touches: touches);
    }
}
