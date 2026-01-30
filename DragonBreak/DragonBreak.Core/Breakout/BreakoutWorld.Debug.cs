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

    // --- AI feel tuning (debug / prototype) ---
    // These are intentionally conservative so AI feels "human" and doesn't jitter.
    private const float AiReactionDelaySeconds = 0.09f;     // small latency
    private const float AiAimSmoothingHz = 10.5f;           // higher = snappier
    private const float AiMaxMoveResponseSeconds = 0.13f;   // larger = softer acceleration

    // Separation: keep paddle centers at least this far apart (derived from sizes each frame).
    private const float AiSeparationStrength = 1.15f;       // repulsion gain
    private const float AiSeparationLookaheadSeconds = 0.18f;

    // Aim "mistake": error increases slightly with distance and ball speed.
    private const float AiBaseAimErrorPixels = 6f;
    private const float AiAimErrorMaxPixels = 26f;

    // Lane behavior: paddles have a preferred anchor to prevent clumping.
    private const float AiLaneHoldStrength = 0.55f;     // 0..1 blend toward lane when not defending
    private const float AiLaneJitterPixels = 8f;        // small idle wander so they don't look robotic

    // Auto-serve (AI only)
    private const float AiAutoServeMinDelaySeconds = 0.35f;
    private const float AiAutoServeMaxDelaySeconds = 0.80f;

    // Per-paddle AI state.
    private struct AiPaddleState
    {
        public float ReactionTimeLeft;
        public Vector2 FilteredTarget;
        public Vector2 NextTarget;
        public int LastBallIndex;
        public float AimErrorX;

        public float ServeDelayLeft;
        public float LastMoveX;
        public float LastMoveY;

        public float LaneJitterX;

        // --- Attack plan (brick targeting + aimed release) ---
        public int TargetBrickIndex;
        public float RetargetBrickTimeLeft;
        public float DesiredServeOffsetX;
        public float DesiredServeOffsetTimeLeft;
        public float ReleaseCooldownLeft;
    }

    private AiPaddleState[] _aiStateByPaddle = Array.Empty<AiPaddleState>();

    private void EnsureAiArraysSized()
    {
        EnsureDebugInitialized();

        int n = _paddles.Count;
        if (n <= 0)
        {
            _aiControlledByPaddle = Array.Empty<bool>();
            _aiLastTargetByPaddle = Array.Empty<Vector2>();
            _aiStateByPaddle = Array.Empty<AiPaddleState>();
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

        if (_aiStateByPaddle.Length != n)
        {
            var old = _aiStateByPaddle;
            _aiStateByPaddle = new AiPaddleState[n];
            for (int i = 0; i < Math.Min(old.Length, n); i++)
                _aiStateByPaddle[i] = old[i];

            // Initialize any new states.
            for (int i = old.Length; i < n; i++)
            {
                _aiStateByPaddle[i] = new AiPaddleState
                {
                    ReactionTimeLeft = 0f,
                    FilteredTarget = _paddles[i].Center,
                    NextTarget = _paddles[i].Center,
                    LastBallIndex = -1,
                    AimErrorX = 0f,
                    ServeDelayLeft = 0f,
                    LastMoveX = 0f,
                    LastMoveY = 0f,
                    LaneJitterX = 0f,

                    TargetBrickIndex = -1,
                    RetargetBrickTimeLeft = 0f,
                    DesiredServeOffsetX = 0f,
                    DesiredServeOffsetTimeLeft = 0f,
                    ReleaseCooldownLeft = 0f,
                };
            }
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

    private int ChooseBallForPaddle(int paddleIndex, Viewport playfield)
    {
        if (_balls.Count == 0)
            return -1;

        var paddle = _paddles[paddleIndex];
        float paddleY = paddle.Center.Y;

        int bestIndex = -1;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < _balls.Count; i++)
        {
            if (i < _ballServing.Count && _ballServing[i])
                continue;

            var b = _balls[i];

            // Ignore extremely slow balls (just treat them as "not a threat" for now).
            float speed = b.Velocity.Length();
            if (speed < 40f)
                continue;

            float dyToPaddle = paddleY - b.Position.Y;
            bool incoming = (b.Velocity.Y * dyToPaddle) > 0f && MathF.Abs(b.Velocity.Y) > 10f;

            // Estimate time-to-cross our Y.
            float t = float.PositiveInfinity;
            if (MathF.Abs(b.Velocity.Y) > 1e-3f)
            {
                float tt = dyToPaddle / b.Velocity.Y;
                if (tt > 0f)
                    t = tt;
            }

            // Score:
            // - Prefer incoming balls
            // - Prefer sooner intercepts
            // - Prefer closer X distance
            float dx = MathF.Abs(b.Position.X - paddle.Center.X);
            float score = (incoming ? 0f : 10_000f) + t * 1200f + dx;

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        // Fallback: just chase nearest ball.
        if (bestIndex < 0)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < _balls.Count; i++)
            {
                if (i < _ballServing.Count && _ballServing[i])
                    continue;
                float d = Vector2.DistanceSquared(_balls[i].Position, _paddles[paddleIndex].Center);
                if (d < best) { best = d; bestIndex = i; }
            }
        }

        return bestIndex;
    }

    private float PredictBallXAtYWithBounces(Ball b, float targetY, Viewport playfield)
    {
        // Predict X at the time the ball reaches targetY, with simple left/right wall bounces.
        // We ignore brick/paddle bounces (context-aware enough for "human" feel).
        if (MathF.Abs(b.Velocity.Y) < 1e-3f)
            return b.Position.X;

        float t = (targetY - b.Position.Y) / b.Velocity.Y;
        if (t < 0f)
            return b.Position.X;

        float rawX = b.Position.X + b.Velocity.X * t;

        float minX = b.Radius;
        float maxX = Math.Max(minX, playfield.Width - b.Radius);
        float width = maxX - minX;
        if (width <= 1e-3f)
            return MathHelper.Clamp(rawX, minX, maxX);

        // Reflect across walls using a triangle wave.
        float u = (rawX - minX) / width; // unbounded
        float m = u % 2f;
        if (m < 0f) m += 2f;
        float tri = m <= 1f ? m : (2f - m);
        return minX + tri * width;
    }

    private float GetLaneAnchorCenterX(int paddleIndex, Viewport playfield)
    {
        int n = Math.Max(1, _paddles.Count);
        float slice = playfield.Width / (float)n;
        float x = (paddleIndex + 0.5f) * slice;
        float half = _paddles[paddleIndex].Size.X * 0.5f;
        return MathHelper.Clamp(x, half, playfield.Width - half);
    }

    /// <summary>
    /// AI-controlled paddles auto-serve their primary ball after a short human-like delay.
    /// </summary>
    private bool AiShouldAutoServe(int paddleIndex, float dt)
    {
        if ((uint)paddleIndex >= (uint)_paddles.Count)
            return false;

        // Only consider the primary ball for this player.
        if (paddleIndex >= _primaryBallIndexByPlayer.Count)
            return false;

        int bi = _primaryBallIndexByPlayer[paddleIndex];
        if ((uint)bi >= (uint)_balls.Count || (uint)bi >= (uint)_ballServing.Count)
            return false;

        if (!_ballServing[bi])
            return false;

        // Don't serve when suppressed (menu resume / etc).
        if (paddleIndex < _launchSuppressedByPlayer.Count && _launchSuppressedByPlayer[paddleIndex])
            return false;

        ref var st = ref _aiStateByPaddle[paddleIndex];

        // Seed a delay the first time we notice serving.
        if (st.ServeDelayLeft <= 0f)
        {
            // Small deterministic-ish spread per paddle so they don't all serve simultaneously.
            float t = ((paddleIndex * 97) % 100) / 100f;
            st.ServeDelayLeft = MathHelper.Lerp(AiAutoServeMinDelaySeconds, AiAutoServeMaxDelaySeconds, t);
        }

        st.ServeDelayLeft -= dt;
        if (st.ServeDelayLeft <= 0f)
        {
            st.ServeDelayLeft = 0f;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Hard post-step overlap resolver (AI only): ensures paddles no longer overlap on X.
    /// This is deterministic and guarantees visual/gameplay sanity even if steering fails.
    /// </summary>
    private void ResolveAiPaddleOverlaps(Viewport playfield)
    {
        if (_paddles.Count <= 1)
            return;

        // Only bother if at least 2 paddles are AI.
        int aiCount = 0;
        for (int i = 0; i < _paddles.Count; i++)
            if (IsAiForPaddle(i)) aiCount++;
        if (aiCount <= 1)
            return;

        // Sort paddle indices by current X (stable).
        int n = _paddles.Count;
        Span<int> idx = n <= 32 ? stackalloc int[n] : new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        idx.Sort((a, b) => _paddles[a].Position.X.CompareTo(_paddles[b].Position.X));

        const float gap = 2f;

        // Left-to-right pass: push right if overlapping.
        for (int k = 1; k < n; k++)
        {
            int i = idx[k];
            int prev = idx[k - 1];

            if (!IsAiForPaddle(i) && !IsAiForPaddle(prev))
                continue;

            var pPrev = _paddles[prev];
            var p = _paddles[i];

            float minX = pPrev.Position.X + pPrev.Size.X + gap;
            if (p.Position.X < minX)
            {
                // Prefer moving AI paddles; if both AI, split. If only one AI, move that one.
                if (IsAiForPaddle(i) && IsAiForPaddle(prev))
                {
                    float overlap = minX - p.Position.X;
                    pPrev.Position.X = MathHelper.Clamp(pPrev.Position.X - overlap * 0.5f, 0f, playfield.Width - pPrev.Size.X);
                    p.Position.X = MathHelper.Clamp(p.Position.X + overlap * 0.5f, 0f, playfield.Width - p.Size.X);
                    _paddles[prev] = pPrev;
                    _paddles[i] = p;
                }
                else if (IsAiForPaddle(i))
                {
                    p.Position.X = MathHelper.Clamp(minX, 0f, playfield.Width - p.Size.X);
                    _paddles[i] = p;
                }
                else if (IsAiForPaddle(prev))
                {
                    float desiredPrev = p.Position.X - pPrev.Size.X - gap;
                    pPrev.Position.X = MathHelper.Clamp(desiredPrev, 0f, playfield.Width - pPrev.Size.X);
                    _paddles[prev] = pPrev;
                }
            }
        }

        // Clamp to world and do one more pass for safety.
        for (int i = 0; i < _paddles.Count; i++)
        {
            var p = _paddles[i];
            p.Position.X = MathHelper.Clamp(p.Position.X, 0f, playfield.Width - p.Size.X);
            _paddles[i] = p;
        }

        // Second pass (cheap) to ensure no residual overlap.
        for (int k = 1; k < n; k++)
        {
            int i = idx[k];
            int prev = idx[k - 1];
            var pPrev = _paddles[prev];
            var p = _paddles[i];
            float minX = pPrev.Position.X + pPrev.Size.X + gap;
            if (p.Position.X < minX)
            {
                p.Position.X = MathHelper.Clamp(minX, 0f, playfield.Width - p.Size.X);
                _paddles[i] = p;
            }
        }
    }

    private (float MoveX, float MoveY) ComputeAiMoveForPaddle(
        int paddleIndex,
        Viewport playfield,
        float dt,
        bool isDefender,
        float laneMinX,
        float laneMaxX,
        float desiredDefendY,
        bool allowLeaveLane)
    {
        EnsureAiArraysSized();

        if ((uint)paddleIndex >= (uint)_paddles.Count)
            return (0f, 0f);

        var paddle = _paddles[paddleIndex];
        ref var st = ref _aiStateByPaddle[paddleIndex];

        dt = MathHelper.Clamp(dt, 0f, 1f / 20f);

        // Lane jitter: keep it tiny, and keep it inside lane.
        if (st.LaneJitterX == 0f)
        {
            float seed = ((paddleIndex * 17 + 3) % 23) / 23f;
            st.LaneJitterX = MathHelper.Lerp(-AiLaneJitterPixels, AiLaneJitterPixels, seed);
        }
        else
        {
            st.LaneJitterX = MathHelper.Lerp(st.LaneJitterX, 0f, 0.12f * dt);
        }

        st.ReactionTimeLeft -= dt;
        if (st.ReactionTimeLeft <= 0f)
        {
            st.ReactionTimeLeft = AiReactionDelaySeconds;

            int bi = st.LastBallIndex; // caller may pre-assign
            if ((uint)bi >= (uint)_balls.Count || (bi < _ballServing.Count && _ballServing[bi]))
                bi = ChooseBallForPaddle(paddleIndex, playfield);

            st.LastBallIndex = bi;

            // Default: hold lane center.
            float laneCenter = (laneMinX + laneMaxX) * 0.5f;
            laneCenter = MathHelper.Clamp(laneCenter, paddle.Size.X * 0.5f, playfield.Width - paddle.Size.X * 0.5f);

            float laneX = MathHelper.Clamp(laneCenter + st.LaneJitterX, laneMinX, laneMaxX);
            Vector2 desired = new(laneX, desiredDefendY);

            if (bi >= 0 && (uint)bi < (uint)_balls.Count)
            {
                var b = _balls[bi];

                // Defender: track intercept; others: stay in lane, but can make small supportive adjustments.
                if (isDefender)
                {
                    float interceptX = PredictBallXAtYWithBounces(b, desiredDefendY, playfield);

                    float dist = MathF.Abs(interceptX - paddle.Center.X);
                    float speed = b.Velocity.Length();
                    float err = AiBaseAimErrorPixels + 0.010f * dist + 0.007f * speed;
                    err = MathHelper.Clamp(err, AiBaseAimErrorPixels, AiAimErrorMaxPixels);

                    if (st.AimErrorX == 0f)
                    {
                        float sign = ((paddleIndex + bi) & 1) == 0 ? -1f : 1f;
                        st.AimErrorX = sign * err;
                    }
                    else
                    {
                        st.AimErrorX = MathHelper.Lerp(st.AimErrorX, MathF.Sign(st.AimErrorX) * err, 0.25f);
                    }

                    float targetX = interceptX + st.AimErrorX;

                    if (!allowLeaveLane)
                        targetX = MathHelper.Clamp(targetX, laneMinX, laneMaxX);

                    desired.X = targetX;

                    // Vertical behavior: if we can move vertically, try to sit on the defend line but follow the ball a bit.
                    // This makes movement look intentional in modes where paddles can move up.
                    float followY = MathHelper.Clamp(b.Position.Y + 110f, 0f, playfield.Height);
                    desired.Y = MathHelper.Lerp(desiredDefendY, followY, 0.18f);
                }
                else
                {
                    // Support paddles: keep lane but drift slightly toward ball X if it's inside their lane.
                    float assistX = b.Position.X;
                    if (assistX >= laneMinX && assistX <= laneMaxX)
                        desired.X = MathHelper.Lerp(laneX, assistX, 0.20f);

                    desired.Y = desiredDefendY;
                }
            }

            // Clamp target to playfield bounds.
            float halfW = paddle.Size.X * 0.5f;
            desired.X = MathHelper.Clamp(desired.X, halfW, playfield.Width - halfW);
            desired.Y = MathHelper.Clamp(desired.Y, 0f + paddle.Size.Y * 0.5f, playfield.Height - paddle.Size.Y * 0.5f);

            st.NextTarget = desired;
        }

        float a = 1f - MathF.Exp(-AiAimSmoothingHz * dt);
        st.FilteredTarget = Vector2.Lerp(st.FilteredTarget, st.NextTarget, a);

        if ((uint)paddleIndex < (uint)_aiLastTargetByPaddle.Length)
            _aiLastTargetByPaddle[paddleIndex] = st.FilteredTarget;

        float dx = st.FilteredTarget.X - paddle.Center.X;
        float dy = st.FilteredTarget.Y - paddle.Center.Y;

        float deadX = Math.Max(10f, paddle.Size.X * 0.10f);
        float deadY = Math.Max(6f, paddle.Size.Y * 0.35f);

        float desiredMoveX = Math.Abs(dx) <= deadX ? 0f : MathHelper.Clamp(dx / 170f, -1f, 1f);
        float desiredMoveY = Math.Abs(dy) <= deadY ? 0f : MathHelper.Clamp(dy / 190f, -1f, 1f);

        float moveA = dt <= 0f ? 1f : MathHelper.Clamp(dt / AiMaxMoveResponseSeconds, 0.05f, 0.65f);
        st.LastMoveX = MathHelper.Lerp(st.LastMoveX, desiredMoveX, moveA);
        st.LastMoveY = MathHelper.Lerp(st.LastMoveY, desiredMoveY, moveA);

        return (st.LastMoveX, st.LastMoveY);
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

    private void SetAiAssignedBall(int paddleIndex, int ballIndex)
    {
        EnsureAiArraysSized();
        if ((uint)paddleIndex >= (uint)_aiStateByPaddle.Length)
            return;

        _aiStateByPaddle[paddleIndex].LastBallIndex = ballIndex;
    }

    private static float ClampServeOffset(float paddleWidth, float ballRadius, float offsetX)
    {
        float minOffset = -(paddleWidth * 0.5f) + ballRadius;
        float maxOffset = (paddleWidth * 0.5f) - ballRadius;
        return MathHelper.Clamp(offsetX, minOffset, maxOffset);
    }

    private int ChooseBrickForPaddle(int paddleIndex, float laneMinX, float laneMaxX)
    {
        // Prefer a brick inside our lane. Bias lower bricks (more reachable), lower HP, and closer to lane center.
        // Avoid bricks that are already very close to the human player's current lane-center (simple awareness).
        if (_bricks.Count == 0)
            return -1;

        float laneCenter = (laneMinX + laneMaxX) * 0.5f;

        float humanBiasLaneCenter = float.NaN;
        if (_paddles.Count > 0)
        {
            // Assume paddle 0 is the primary human unless AI-all is enabled.
            if (!IsAiForPaddle(0) && _paddles.Count > 0)
                humanBiasLaneCenter = _paddles[0].Center.X;
        }

        int best = -1;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < _bricks.Count; i++)
        {
            var b = _bricks[i];
            if (!b.IsAlive)
                continue;

            float x = b.Bounds.Center.X;
            if (x < laneMinX || x > laneMaxX)
                continue;

            // Bias away from the human's immediate area so AI "helps" rather than duplicates.
            float humanPenalty = 0f;
            if (!float.IsNaN(humanBiasLaneCenter))
            {
                float dHuman = Math.Abs(x - humanBiasLaneCenter);
                // If the brick is within ~1/6 of screen width of the human, penalize.
                humanPenalty = dHuman < 140f ? (140f - dHuman) * 4f : 0f;
            }

            // Lower bricks should be preferred (larger y means lower on screen).
            float y = b.Bounds.Center.Y;

            // HP preference (lower HP first).
            float hp = Math.Max(1, b.HitPoints);

            float dxLane = Math.Abs(x - laneCenter);

            // Score: lower is better.
            // We strongly prefer lower bricks and lower HP, then closeness to lane center.
            float score = (1000f - y) * 1.1f + hp * 140f + dxLane * 1.4f + humanPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    private float ComputeDesiredServeOffsetForBrick(int paddleIndex, int brickIndex)
    {
        // Compute an offset on the paddle for a caught/served ball so the eventual launch has a good chance
        // to head toward the target brick. We approximate by aiming the ball from paddle -> brick.
        // This is intentionally imperfect; it should feel human.
        if ((uint)paddleIndex >= (uint)_paddles.Count)
            return 0f;
        if ((uint)brickIndex >= (uint)_bricks.Count || !_bricks[brickIndex].IsAlive)
            return 0f;

        var paddle = _paddles[paddleIndex];
        var brick = _bricks[brickIndex];

        Vector2 from = paddle.Center;
        Vector2 to = new Vector2(brick.Bounds.Center.X, brick.Bounds.Center.Y);

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        if (Math.Abs(dy) < 1e-3f)
            dy = -1f;

        // Desired horizontal speed proportion (very rough): dx/dy limited.
        float desiredSlope = dx / Math.Abs(dy);
        desiredSlope = MathHelper.Clamp(desiredSlope, -0.9f, 0.9f);

        // Convert slope to a paddle offset. Larger offset => more horizontal angle.
        // Tuned: about half paddle width yields strong angles.
        float offsetX = desiredSlope * (paddle.Size.X * 0.42f);

        // Add variance so it doesn't look robotic.
        float jitter = (((paddleIndex * 37 + brickIndex * 19) % 9) - 4) * 6f; // -24..+24
        offsetX += jitter;

        // Clamp to safe.
        float ballRadius = 8f;
        if (paddleIndex < _primaryBallIndexByPlayer.Count)
        {
            int bi = _primaryBallIndexByPlayer[paddleIndex];
            if ((uint)bi < (uint)_balls.Count)
                ballRadius = _balls[bi].Radius;
        }

        return ClampServeOffset(paddle.Size.X, ballRadius, offsetX);
    }

    private bool TryGetAliveBrickCenter(int brickIndex, out Vector2 center)
    {
        center = default;
        if ((uint)brickIndex >= (uint)_bricks.Count)
            return false;
        var b = _bricks[brickIndex];
        if (!b.IsAlive)
            return false;
        center = new Vector2(b.Bounds.Center.X, b.Bounds.Center.Y);
        return true;
    }
}
