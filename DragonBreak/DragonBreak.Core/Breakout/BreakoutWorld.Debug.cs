#nullable enable
using System;
using DragonBreak.Core.Breakout.Entities;
using DragonBreak.Core.Breakout.Ui;
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DragonBreak.Core.Breakout;

public sealed partial class BreakoutWorld
{
    // --- Debug helpers/state ---
    private int _debugPendingJumpLevel = 1; // 1-based for UI friendliness
    private int _debugPowerUpIndex;

    // AI control (debug): per-paddle + auto mode.
    private bool[] _aiControlledByPaddle = Array.Empty<bool>();
    private bool _aiAllPaddles;

    // AI debug overlay.
    private bool _debugDrawAi;

    // Per-paddle: last AI target point (playfield-local) for debug draw.
    private Vector2[] _aiLastTargetByPaddle = Array.Empty<Vector2>();

    // Keep track of whether we already initialized our debug arrays for the current run.
    private bool _debugInitialized;

    private void EnsureDebugInitialized()
    {
        if (_debugInitialized)
            return;

        _aiControlledByPaddle = Array.Empty<bool>();
        _aiLastTargetByPaddle = Array.Empty<Vector2>();

        _aiAllPaddles = false;
        _debugDrawAi = false;
        _debugPendingJumpLevel = 1;
        _debugPowerUpIndex = 0;

        _debugInitialized = true;
    }

    private void EnsureAiArraysSized()
    {
        EnsureDebugInitialized();

        int n = _paddles.Count;
        if (n <= 0)
        {
            _aiControlledByPaddle = Array.Empty<bool>();
            _aiLastTargetByPaddle = Array.Empty<Vector2>();
            return;
        }

        if (_aiControlledByPaddle.Length != n)
        {
            var old = _aiControlledByPaddle;
            _aiControlledByPaddle = new bool[n];
            for (int i = 0; i < Math.Min(old.Length, n); i++)
                _aiControlledByPaddle[i] = old[i];
        }

        if (_aiLastTargetByPaddle.Length != n)
        {
            var old = _aiLastTargetByPaddle;
            _aiLastTargetByPaddle = new Vector2[n];
            for (int i = 0; i < Math.Min(old.Length, n); i++)
                _aiLastTargetByPaddle[i] = old[i];
        }
    }

    private bool IsAiForPaddle(int paddleIndex)
    {
        EnsureAiArraysSized();

        if (_aiAllPaddles)
            return true;

        if ((uint)paddleIndex >= (uint)_aiControlledByPaddle.Length)
            return false;

        // Default behavior: keep paddle 0 as human unless explicitly set to AI.
        return _aiControlledByPaddle[paddleIndex];
    }

    private void ToggleAiForPaddle(int paddleIndex)
    {
        EnsureAiArraysSized();

        if ((uint)paddleIndex >= (uint)_aiControlledByPaddle.Length)
            return;

        _aiControlledByPaddle[paddleIndex] = !_aiControlledByPaddle[paddleIndex];
    }

    // Backwards-compat wrappers (pause menu still uses P2/P3/P4 labels).
    private bool IsAiForPlayer(int playerIndex) => IsAiForPaddle(playerIndex);
    private void ToggleAiForPlayer(int playerIndex) => ToggleAiForPaddle(playerIndex);

    private (float MoveX, float MoveY) ComputeAiMoveForPaddle(int paddleIndex, Viewport playfield)
    {
        EnsureAiArraysSized();

        if ((uint)paddleIndex >= (uint)_paddles.Count)
            return (0f, 0f);

        // Choose a target point for this paddle.
        // Strategy:
        // 1) Prefer an incoming ball (moving toward the paddle area).
        // 2) Aim for the predicted X intercept when the ball reaches the paddle's Y.
        // 3) Also bias Y toward the ball's Y when the paddle isn't at the bottom (future-proof for multi-side paddles).

        var paddle = _paddles[paddleIndex];

        Vector2 target = paddle.Center;
        float bestScore = float.PositiveInfinity;
        bool found = false;

        float paddleY = paddle.Center.Y;

        for (int i = 0; i < _balls.Count; i++)
        {
            if (i < _ballServing.Count && _ballServing[i])
                continue;

            var b = _balls[i];

            // "Incoming" means the ball is moving toward the paddle's Y.
            // If paddle is below the ball, incoming is downward; if above, incoming is upward.
            float dyToPaddle = paddleY - b.Position.Y;
            bool incoming = (b.Velocity.Y * dyToPaddle) > 0f && MathF.Abs(b.Velocity.Y) > 8f;

            // Predict intercept at paddleY if possible.
            float t = 0f;
            bool hasIntercept = MathF.Abs(b.Velocity.Y) > 1e-3f;
            if (hasIntercept)
                t = (paddleY - b.Position.Y) / b.Velocity.Y;

            // If intercept time is negative, ball is moving away.
            if (hasIntercept && t < 0f)
                hasIntercept = false;

            float predictedX = b.Position.X;
            if (hasIntercept)
            {
                predictedX = b.Position.X + b.Velocity.X * t;
                predictedX = MathHelper.Clamp(predictedX, b.Radius, playfield.Width - b.Radius);
            }

            // Build a target. If we can intercept, aim at intercept X on our Y; else chase ball.
            Vector2 candidateTarget = hasIntercept
                ? new Vector2(predictedX, paddleY)
                : b.Position;

            // Score candidates: prefer incoming, then shorter distance to paddle.
            float d = Vector2.DistanceSquared(candidateTarget, paddle.Center);
            float score = d + (incoming ? 0f : 2_000_000f);

            if (!found || score < bestScore)
            {
                found = true;
                bestScore = score;
                target = candidateTarget;
            }
        }

        // If no balls exist, just drift toward a sane default (near bottom).
        if (!found)
            target = new Vector2(paddle.Center.X, playfield.Height - PaddleBottomPaddingPixels);

        if ((uint)paddleIndex < (uint)_aiLastTargetByPaddle.Length)
            _aiLastTargetByPaddle[paddleIndex] = target;

        // Convert target into normalized move inputs (-1..1 each).
        float dx = target.X - paddle.Center.X;
        float dy = target.Y - paddle.Center.Y;

        float deadX = Math.Max(6f, paddle.Size.X * 0.06f);
        float deadY = Math.Max(4f, paddle.Size.Y * 0.20f);

        float moveX = Math.Abs(dx) <= deadX ? 0f : MathHelper.Clamp(dx / 120f, -1f, 1f);
        float moveY = Math.Abs(dy) <= deadY ? 0f : MathHelper.Clamp(dy / 120f, -1f, 1f);

        // Mild vertical damping so AI doesn't jitter when it only needs tiny Y corrections.
        moveY = MathHelper.Clamp(moveY, -0.75f, 0.75f);

        return (moveX, moveY);
    }


    private static PowerUpType[] DebugPowerUpOrder =>
    [
        PowerUpType.ExpandPaddle,
        PowerUpType.SlowBall,
        PowerUpType.FastBall,
        PowerUpType.MultiBall,
        PowerUpType.ScoreBoost,
        PowerUpType.ScoreBurst,
        PowerUpType.ExtraLife,
    ];

    private void DebugSpawnSelectedPowerUp(Viewport vp)
    {
        if (_paddles.Count <= 0)
            return;

        var arr = DebugPowerUpOrder;
        if (arr.Length == 0)
            return;

        int idx = Math.Clamp(_debugPowerUpIndex, 0, arr.Length - 1);
        _debugPowerUpIndex = idx;

        var type = arr[idx];
        DebugSpawnPowerUp(type, vp);
        ShowToast($"Spawned: {GetPowerUpToastText(type)}", ToastDurationSeconds);

        // Cycle next.
        _debugPowerUpIndex = (_debugPowerUpIndex + 1) % arr.Length;
    }

    private void DebugSpawnPowerUp(PowerUpType type, Viewport fullViewport)
    {
        var playfield = GetPlayfieldViewport(fullViewport);
        var pos = new Vector2(playfield.Width * 0.5f, Math.Max(16f, playfield.Height * 0.22f));
        _powerUps.Add(new PowerUp(type, pos));
    }

    private void DebugJumpToLevel(int targetLevelIndex, Viewport fullViewport)
    {
        targetLevelIndex = Math.Max(0, targetLevelIndex);
        _levelIndex = targetLevelIndex;

        var playfield = GetPlayfieldViewport(fullViewport);
        _powerUps.Clear();
        ClearEffects();
        LoadLevel(playfield, _levelIndex);
        ResetBallOnPaddle();

        ShowToast($"Jumped to Level {_levelIndex + 1}", ToastDurationSeconds);
    }

    // Debug UI for jump-to-level (modal)
    private readonly DebugJumpLevelScreen _debugJumpLevelScreen = new();
    private WorldMode _returnModeAfterDebugJump = WorldMode.Paused;

    private void EnterDebugJumpLevel(WorldMode returnTo)
    {
        EnsureDebugInitialized();
        _returnModeAfterDebugJump = returnTo;

        // Sync current pending value into the screen (1-based).
        _debugJumpLevelScreen.Level = Math.Max(1, _debugPendingJumpLevel);

        _mode = WorldMode.DebugJumpLevel;

        // Prevent digit spam on entry.
        _prevDebugKeyboard = Keyboard.GetState();
    }

    private void UpdateDebugJumpLevel(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        EnsureDebugInitialized();

        // Keep screen + state in sync (1-based).
        _debugJumpLevelScreen.Level = Math.Max(1, _debugPendingJumpLevel);

        // --- Fallback raw input handling (robust across platforms) ---
        // Some platforms/controllers can fail to produce menu navigation flags. Always support:
        // - Keyboard arrows
        // - GamePad DPad + left stick
        var ksRaw = Keyboard.GetState();
        bool RawPressed(Keys k) => ksRaw.IsKeyDown(k) && !_prevDebugKeyboard.IsKeyDown(k);

        // Arrow keys behave like a controller.
        if (RawPressed(Keys.Left)) _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel - 1);
        if (RawPressed(Keys.Right)) _debugPendingJumpLevel = _debugPendingJumpLevel + 1;
        if (RawPressed(Keys.Up)) _debugPendingJumpLevel = _debugPendingJumpLevel + 10;
        if (RawPressed(Keys.Down)) _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel - 10);

        // Controller fallback: any connected pad can drive the UI.
        for (int pi = 0; pi < 4; pi++)
        {
            var pad = GamePad.GetState((PlayerIndex)pi);
            var prev = _prevDebugPadByPlayer[pi];

            bool padLeft = pad.IsConnected && (pad.DPad.Left == ButtonState.Pressed || pad.ThumbSticks.Left.X <= -0.55f);
            bool padRight = pad.IsConnected && (pad.DPad.Right == ButtonState.Pressed || pad.ThumbSticks.Left.X >= 0.55f);
            bool padUp = pad.IsConnected && (pad.DPad.Up == ButtonState.Pressed || pad.ThumbSticks.Left.Y >= 0.55f);
            bool padDown = pad.IsConnected && (pad.DPad.Down == ButtonState.Pressed || pad.ThumbSticks.Left.Y <= -0.55f);

            bool prevLeft = prev.IsConnected && (prev.DPad.Left == ButtonState.Pressed || prev.ThumbSticks.Left.X <= -0.55f);
            bool prevRight = prev.IsConnected && (prev.DPad.Right == ButtonState.Pressed || prev.ThumbSticks.Left.X >= 0.55f);
            bool prevUp = prev.IsConnected && (prev.DPad.Up == ButtonState.Pressed || prev.ThumbSticks.Left.Y >= 0.55f);
            bool prevDown = prev.IsConnected && (prev.DPad.Down == ButtonState.Pressed || prev.ThumbSticks.Left.Y <= -0.55f);

            if (padLeft && !prevLeft) _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel - 1);
            if (padRight && !prevRight) _debugPendingJumpLevel = _debugPendingJumpLevel + 1;
            if (padUp && !prevUp) _debugPendingJumpLevel = _debugPendingJumpLevel + 10;
            if (padDown && !prevDown) _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel - 10);

            // Track for edge detection.
            _prevDebugPadByPlayer[pi] = pad;
        }

        // Touch controls:
        // - Tap back button: cancel
        // - Tap left third: -1
        // - Tap right third: +1
        // - Tap top third: +10
        // - Tap bottom third: -10
        // - Tap center: confirm
        if (inputs is { Length: > 0 } && inputs[0].Touches.TryGetBegan(out var tap) && tap.IsTap)
        {
            float x = tap.X01 * vp.Width;
            float y = tap.Y01 * vp.Height;

            if (GetBackButtonRectForTouch(vp).Contains((int)x, (int)y))
            {
                _mode = _returnModeAfterDebugJump;
                _prevDebugKeyboard = ksRaw;
                return;
            }

            if (x < vp.Width * 0.33f)
                _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel - 1);
            else if (x > vp.Width * 0.66f)
                _debugPendingJumpLevel = _debugPendingJumpLevel + 1;
            else if (y < vp.Height * 0.33f)
                _debugPendingJumpLevel = _debugPendingJumpLevel + 10;
            else if (y > vp.Height * 0.66f)
                _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel - 10);
            else
            {
                // Center = confirm
                DebugJumpToLevel(_debugPendingJumpLevel - 1, vp);
                _mode = WorldMode.Playing;
                _prevDebugKeyboard = ksRaw;
                return;
            }

            _debugJumpLevelScreen.Level = _debugPendingJumpLevel;
        }

        // Controller/keyboard via menu mappings.
        _debugJumpLevelScreen.Update(inputs ?? Array.Empty<DragonBreakInput>(), vp, dt);
        switch (_debugJumpLevelScreen.ConsumeAction())
        {
            case DebugJumpLevelScreen.JumpAction.Cancel:
                _mode = _returnModeAfterDebugJump;
                _prevDebugKeyboard = ksRaw;
                return;

            case DebugJumpLevelScreen.JumpAction.Confirm:
                _debugPendingJumpLevel = Math.Max(1, _debugJumpLevelScreen.Level);
                DebugJumpToLevel(_debugPendingJumpLevel - 1, vp);
                _mode = WorldMode.Playing;
                _prevDebugKeyboard = ksRaw;
                return;

            case DebugJumpLevelScreen.JumpAction.None:
            default:
                break;
        }

        // Keyboard direct digit entry.
        var ks = ksRaw;
        bool WasPressed(Keys k) => ks.IsKeyDown(k) && !_prevDebugKeyboard.IsKeyDown(k);

        if (WasPressed(Keys.Escape))
        {
            _mode = _returnModeAfterDebugJump;
        }
        else if (WasPressed(Keys.Enter))
        {
            DebugJumpToLevel(_debugPendingJumpLevel - 1, vp);
            _mode = WorldMode.Playing;
        }
        else
        {
            if (WasPressed(Keys.Back) || WasPressed(Keys.Delete))
            {
                _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel / 10);
            }
            else
            {
                // Append digit.
                for (int d = 0; d <= 9; d++)
                {
                    var key = (Keys)((int)Keys.D0 + d);
                    var np = (Keys)((int)Keys.NumPad0 + d);

                    if (WasPressed(key) || WasPressed(np))
                    {
                        long next = (long)_debugPendingJumpLevel * 10 + d;
                        if (next <= 9999)
                            _debugPendingJumpLevel = (int)next;
                        break;
                    }
                }
            }
        }

        _debugPendingJumpLevel = Math.Max(1, _debugPendingJumpLevel);
        _debugJumpLevelScreen.Level = _debugPendingJumpLevel;
        _prevDebugKeyboard = ks;
    }
}
