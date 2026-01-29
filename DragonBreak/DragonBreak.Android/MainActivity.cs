using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using DragonBreak.Core;
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;

namespace DragonBreak.Android;

[Activity(
    Label = "DragonBreak",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.ScreenSize,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen")]
public class MainActivity : Microsoft.Xna.Framework.AndroidGameActivity
{
    private DragonBreakGame? _game;

    protected override void OnCreate(Bundle? bundle)
    {
        base.OnCreate(bundle);


        // Create the game instance.
        _game = new DragonBreakGame();

        // Touch input.
        TouchPanel.EnabledGestures = GestureType.None;

        // Provide a delegate that will be called with the current viewport.
        _game.TouchInjector = GetTouchInput;

        // Retrieve the MonoGame View and set it as the content view.
        var view = (View)_game.Services.GetService(typeof(View))!;
        SetContentView(view);

        // Start the game loop.
        _game.Run();
    }

    protected override void OnDestroy()
    {
        _game?.Dispose();
        _game = null;
        base.OnDestroy();
    }

    private static TouchState GetTouchInput(Viewport viewport)
    {
        TouchCollection touches = TouchPanel.GetState();

        if (touches.Count <= 0)
            return TouchState.Empty;

        var state = new TouchState();

        for (int i = 0; i < touches.Count; i++)
        {
            var t = touches[i];

            TouchPhase phase = t.State switch
            {
                TouchLocationState.Pressed => TouchPhase.Began,
                TouchLocationState.Moved => TouchPhase.Moved,
                TouchLocationState.Released => TouchPhase.Ended,
                TouchLocationState.Invalid => TouchPhase.Canceled,
                _ => TouchPhase.Moved,
            };

            float x01 = viewport.Width > 0 ? t.Position.X / viewport.Width : 0.5f;
            float y01 = viewport.Height > 0 ? t.Position.Y / viewport.Height : 0.5f;

            // Treat Pressed as a tap candidate; higher level code can decide what to do.
            bool isTap = t.State == TouchLocationState.Pressed;

            state.Add(new TouchPoint(t.Id, phase, x01, y01, isTap));
        }

        return state;
    }
}