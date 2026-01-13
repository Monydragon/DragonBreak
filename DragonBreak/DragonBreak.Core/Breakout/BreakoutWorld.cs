#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Audio;
using DragonBreak.Core.Breakout.Entities;
using DragonBreak.Core.Breakout.Ui;
using DragonBreak.Core.Graphics;
using DragonBreak.Core.Input;
using DragonBreak.Core.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Security.Cryptography;

namespace DragonBreak.Core.Breakout;

public sealed class BreakoutWorld
{
    private readonly Random _rng = new();

    private Texture2D _pixel = null!;
    private SpriteFont? _hudFont;

    private readonly List<Paddle> _paddles = new();
    private readonly List<Ball> _balls = new();
    private readonly List<bool> _ballServing = new();

    // Per-ball: horizontal offset from owning paddle center while in serving/caught state.
    // This preserves left/middle/right catch position (serving state otherwise re-centers every frame).
    private readonly List<float> _ballServeOffsetX = new();

    // Per-ball: time since last launch/serve (seconds). Used for a short speed ramp after launch.
    private readonly List<float> _ballLaunchAgeSeconds = new();

    // Launch feel tuning.
    private const float ServeRampDurationSeconds = 1.25f;
    private const float ServeStartSpeedMultiplier = 0.78f;

    // How strongly paddle horizontal velocity influences the launch direction.
    // Units: (ball horizontal speed) = paddleVelX * factor.
    private const float ServePaddleMomentumFactor = 0.55f;

    // Clamp to avoid extreme side-launches.
    private const float ServeMaxAngleDegrees = 55f;

    private readonly List<Brick> _bricks = new();
    private readonly List<PowerUp> _powerUps = new();

    private enum WorldMode
    {
        Menu,
        Settings,
        Playing,
        LevelInterstitial,
        Paused,
    }

    private readonly struct DifficultyPreset
    {
        public readonly string Name;
        public readonly int StartingLives;
        public readonly float PaddleSpeed;
        public readonly float BallBaseSpeed;
        public readonly float SpeedRampPerLevel;
        public readonly int MaxBrickHp;
        public readonly float PowerUpDropChance;

        public DifficultyPreset(
            string name,
            int startingLives,
            float paddleSpeed,
            float ballBaseSpeed,
            float speedRampPerLevel,
            int maxBrickHp,
            float powerUpDropChance)
        {
            Name = name;
            StartingLives = startingLives;
            PaddleSpeed = paddleSpeed;
            BallBaseSpeed = ballBaseSpeed;
            SpeedRampPerLevel = speedRampPerLevel;
            MaxBrickHp = maxBrickHp;
            PowerUpDropChance = powerUpDropChance;
        }
    }

    private static readonly DifficultyPreset[] Presets =
    [
        new DifficultyPreset("casual",    startingLives: 10, paddleSpeed: 720f, ballBaseSpeed: 340f, speedRampPerLevel: 14f, maxBrickHp: 3, powerUpDropChance: 0.22f),
        new DifficultyPreset("very easy", startingLives: 8, paddleSpeed: 700f, ballBaseSpeed: 370f, speedRampPerLevel: 16f, maxBrickHp: 3, powerUpDropChance: 0.20f),
        new DifficultyPreset("easy",      startingLives: 5, paddleSpeed: 680f, ballBaseSpeed: 400f, speedRampPerLevel: 18f, maxBrickHp: 4, powerUpDropChance: 0.18f),
        new DifficultyPreset("normal",    startingLives: 3, paddleSpeed: 650f, ballBaseSpeed: 420f, speedRampPerLevel: 22f, maxBrickHp: 5, powerUpDropChance: 0.16f),
        new DifficultyPreset("hard",      startingLives: 2, paddleSpeed: 640f, ballBaseSpeed: 455f, speedRampPerLevel: 26f, maxBrickHp: 6, powerUpDropChance: 0.14f),
        new DifficultyPreset("very hard", startingLives: 2, paddleSpeed: 620f, ballBaseSpeed: 490f, speedRampPerLevel: 30f, maxBrickHp: 7, powerUpDropChance: 0.12f),
        new DifficultyPreset("extreme",   startingLives: 1, paddleSpeed: 600f, ballBaseSpeed: 535f, speedRampPerLevel: 34f, maxBrickHp: 8, powerUpDropChance: 0.10f),
    ];

    private WorldMode _mode;
    private int _selectedPresetIndex;
    private DifficultyPreset _preset;

    private DifficultyId _selectedDifficultyId = DifficultyId.Normal;

    private bool IsCasualNoLose => _selectedDifficultyId == DifficultyId.Casual;

    // Level interstitial
    private string _levelInterstitialLine = string.Empty;
    private bool _levelInterstitialWasWin;
    private float _interstitialTimeLeft;

    private static readonly string[] LevelWinLines =
    [
        "Level cleared. Bricks: 0. Ego: 100.",
        "You did it. The bricks are writing a complaint.",
        "Nice. That was legally considered 'skilled'.",
        "Bricks eliminated. Dragons impressed.",
        "You win this round, rectangle society.",
    ];

    private static readonly string[] LevelFailLines =
    [
        "Ball lost. Gravity remains undefeated.",
        "The void accepts your donation.",
        "Bonk was not optional.",
        "Physics says: 'try again'.",
        "That ball went on an adventure.",
    ];

    // Track which ball index is the primary ball for each player.
    // Losing a non-primary ball never costs a life.
    private readonly List<int> _primaryBallIndexByPlayer = new();

    private int _activePlayerCount = 1;

    private Vector2 _basePaddleSize;

    // Difficulty affects baseline paddle width (before any temporary power-ups).
    private float _difficultyPaddleWidthMultiplier = 1f;

    // Powerup effects (simple timed multipliers)
    private float _paddleWidthMultiplier = 1f;
    private float _paddleWidthTimeLeft;

    private float _ballSpeedMultiplier = 1f;
    private float _ballSpeedTimeLeft;

    private float _scoreMultiplier = 1f;
    private float _scoreMultiplierTimeLeft;

    private readonly List<int> _livesByPlayer = new();
    private readonly List<int> _scoreByPlayer = new();

    // Catch/serve control
    private readonly List<bool> _catchArmedByPlayer = new();
    private readonly List<bool> _catchArmedConsumedByPlayer = new();

    // Suppress launching while the player is still holding the launch/catch button from a prior screen.
    private readonly List<bool> _launchSuppressedByPlayer = new();

    // Per-ball: was this ball caught (stuck) via catch mechanic (vs initial serve)?
    private readonly List<bool> _ballCaught = new();

    // Powerup toast
    private string _toastText = string.Empty;
    private float _toastTimeLeft;
    private const float ToastDurationSeconds = 1.35f;

    private enum GameMode
    {
        Arcade,
        Story,
        Puzzle,
    }

    private GameMode _selectedGameMode = GameMode.Arcade;
    private int _selectedPlayers = 1;

    private enum MenuItem
    {
        GameMode,
        Players,
        Difficulty,
        Settings,
        Start,
    }

    private MenuItem _menuItem = MenuItem.Start;

    // Menu debouncing so navigation only moves one step per input.
    private bool _menuUpConsumed;
    private bool _menuDownConsumed;
    private bool _menuLeftConsumed;
    private bool _menuRightConsumed;

    // Settings debouncing so left/right changes only move one step per input.
    private bool _settingsLeftConsumed;
    private bool _settingsRightConsumed;

    private const float MenuAxisDeadzone = 0.55f;

    // Paddle movement configuration (easy to tweak):
    // 0.5f means paddles can move up until their top edge reaches halfway down the screen.
    private const float PaddleMaxUpScreenFraction = 0.5f;
    private const float PaddleBottomPaddingPixels = 60f;


    // Multiplayer brick ownership rules only apply in non-Arcade modes.
    private bool IsClassicMode => _selectedGameMode == GameMode.Arcade;
    private bool IsOwnedBricksMode => _activePlayerCount > 1 && !IsClassicMode;

    private static readonly Color[] PlayerBaseColors =
    [
        new Color(80, 180, 255),   // P1: blue
        new Color(255, 120, 80),   // P2: orange
        new Color(120, 235, 120),  // P3: green
        new Color(210, 120, 255),  // P4: purple
    ];

    // Settings menu
    private SettingsManager? _settings;
    private DisplayModeService? _displayModes;
    private GraphicsDeviceManager? _graphics;

    private enum SettingsItem
    {
        WindowMode,
        Resolution,
        VSync,
        MasterVolume,
        BgmVolume,
        SfxVolume,

        HudEnabled,
        HudScale,
        HudP1,
        HudP2,
        HudP3,
        HudP4,

        ContinueMode,
        AutoContinueSeconds,

        LevelSeed,
        LevelSeedRandomize,
        LevelSeedReset,

        Apply,
        Cancel,
    }

    private SettingsItem _settingsItem = SettingsItem.Apply;

    // Cached resolution list (once graphics device exists)
    private List<ResolutionOption> _resolutions = new();
    private int _resolutionIndex;

    private readonly BreakoutAudio _audio = new();

    // Pause menu UI state
    private readonly PauseMenuScreen _pauseMenu = new();

    private TimeSpan _totalTime;

    private int _levelIndex;

    // A dedicated top UI bar that is not part of the playable area.
    // Gameplay (paddles/balls/bricks) is simulated in a playfield viewport below this.
    private const int TopHudBarHeightPixels = 96;

    private static Viewport GetPlayfieldViewport(Viewport full)
    {
        // Keep at least a 1px-tall playfield to avoid negative sizes on tiny windows.
        int y = Math.Clamp(TopHudBarHeightPixels, 0, Math.Max(0, full.Height - 1));
        int height = Math.Max(1, full.Height - y);

        return new Viewport(full.X, full.Y + y, full.Width, height);
    }

    private static int GetHudBarHeight(Viewport full)
        => Math.Clamp(TopHudBarHeightPixels, 0, Math.Max(0, full.Height - 1));

    public void Load(GraphicsDevice graphicsDevice, ContentManager content)
    {
        _pixel = PixelTexture.Create(graphicsDevice);

        // Font is optional (Content pipeline may not be available on all platforms yet)
        try
        {
            _hudFont = content.Load<SpriteFont>("Fonts/Hud");
        }
        catch
        {
            _hudFont = null;
        }

        // Audio (safe even if some optional SFX aren't present yet)
        try
        {
            _audio.Load(content);
            _audio.PlayBgmLoop();
        }
        catch
        {
            // Don't fail loading the game if audio content isn't available.
        }

        _mode = WorldMode.Menu;

        // Default difficulty selection comes from settings when available.
        // If not wired yet, keep normal.
        _selectedDifficultyId = DifficultyId.Normal;
        _selectedPresetIndex = DifficultyToPresetIndex(_selectedDifficultyId);
        _preset = Presets[_selectedPresetIndex];

        // Don't start gameplay here. Menu should be the first screen.
        // We'll create paddles/balls/level only after the player hits Start.
        _paddles.Clear();
        _balls.Clear();
        _ballServing.Clear();
        _bricks.Clear();
        _powerUps.Clear();

        _menuItem = MenuItem.Start;

        // Start with menu visible.
        _mode = WorldMode.Menu;
    }

    public void Load(GraphicsDevice graphicsDevice, ContentManager content, GameServiceContainer services)
    {
        // Back-compat: call existing Load, then wire services.
        Load(graphicsDevice, content);
        WireServices(services);
    }

    private void WireServices(GameServiceContainer services)
    {
        _settings = services.GetService(typeof(SettingsManager)) as SettingsManager;
        _displayModes = services.GetService(typeof(DisplayModeService)) as DisplayModeService;
        _graphics = services.GetService(typeof(GraphicsDeviceManager)) as GraphicsDeviceManager;

        if (_graphics != null && _displayModes != null)
        {
            _resolutions = new List<ResolutionOption>(_displayModes.GetSupportedResolutions(_graphics));
        }

        SyncSelectionsFromSettings();
    }

    private void StartNewGame(Viewport vp, DifficultyPreset preset)
    {
        _preset = preset;

        _levelIndex = 0;

        _livesByPlayer.Clear();
        _scoreByPlayer.Clear();
        for (int i = 0; i < _activePlayerCount; i++)
        {
            // Casual: infinite balls, but keep a 1 sentinel internally.
            _livesByPlayer.Add(IsCasualNoLose ? 1 : _preset.StartingLives);
            _scoreByPlayer.Add(0);
        }

        _powerUps.Clear();
        ClearEffects();

        // Create entities using the playable area only.
        var playfield = GetPlayfieldViewport(vp);
        CreateEntitiesForViewport(playfield);
        LoadLevel(playfield, _levelIndex);
        ResetBallOnPaddle();

        // Primary balls: one per player (the initial balls created in CreateEntitiesForViewport).
        _primaryBallIndexByPlayer.Clear();
        for (int i = 0; i < _activePlayerCount; i++)
            _primaryBallIndexByPlayer.Add(i);

        // Catch state per player.
        _catchArmedByPlayer.Clear();
        _catchArmedConsumedByPlayer.Clear();
        _launchSuppressedByPlayer.Clear();
        for (int i = 0; i < _activePlayerCount; i++)
        {
            _catchArmedByPlayer.Add(false);
            _catchArmedConsumedByPlayer.Add(false);
            _launchSuppressedByPlayer.Add(true);
        }

        // Interstitial state reset
        _interstitialTimeLeft = 0f;
        _levelInterstitialLine = string.Empty;
        _levelInterstitialWasWin = false;

        _toastText = string.Empty;
        _toastTimeLeft = 0f;
    }

    private void ClearEffects()
    {
        _paddleWidthMultiplier = 1f;
        _paddleWidthTimeLeft = 0f;

        _ballSpeedMultiplier = 1f;
        _ballSpeedTimeLeft = 0f;

        _scoreMultiplier = 1f;
        _scoreMultiplierTimeLeft = 0f;
    }

    private void CreateEntitiesForViewport(Viewport vp)
    {
        _paddles.Clear();
        _balls.Clear();
        _ballServing.Clear();
        _ballCaught.Clear();

        _basePaddleSize = new Vector2(Math.Max(120, vp.Width / 7), 20);

        // All paddles start on the bottom row, spread left-to-right.
        // Slight overlap is allowed by making per-player spacing smaller than paddle width.
        float y = vp.Height - PaddleBottomPaddingPixels;
        float maxY = y;
        float minY = Math.Max(0f, vp.Height * PaddleMaxUpScreenFraction);

        int playerCount = Math.Max(1, _activePlayerCount);
        float overlapFactor = 0.90f; // 90% spacing -> 10% overlap potential
        float spacing = (vp.Width / (float)playerCount) * overlapFactor;

        for (int i = 0; i < playerCount; i++)
        {
            float x;
            if (playerCount == 1)
            {
                x = (vp.Width - _basePaddleSize.X) * 0.5f;
            }
            else
            {
                // Center each paddle within its slice.
                float sliceCenter = (i + 0.5f) * (vp.Width / (float)playerCount);
                // Apply overlap spacing by nudging towards left-to-right distribution.
                float nudgedCenter = (playerCount == 1) ? sliceCenter : (i * spacing + spacing * 0.5f);
                // Blend between true slice center and nudged center to keep things reasonable in narrow windows.
                float centerX = MathHelper.Lerp(sliceCenter, nudgedCenter, 0.65f);
                x = centerX - _basePaddleSize.X * 0.5f;
            }

            x = MathHelper.Clamp(x, 0f, vp.Width - _basePaddleSize.X);
            var paddlePos = new Vector2(x, y);
            _paddles.Add(new Paddle(paddlePos, _basePaddleSize, _preset.PaddleSpeed));

            var ball = new Ball(_paddles[i].Center - new Vector2(0, 18), radius: 8f, ownerPlayerIndex: i, isExtraBall: false)
            {
                DrawColor = PlayerBaseColors[Math.Clamp(i, 0, PlayerBaseColors.Length - 1)]
            };
            _balls.Add(ball);
            _ballServing.Add(true);
            _ballCaught.Add(false);
        }

        // Ensure paddles are within vertical limits immediately.
        for (int i = 0; i < _paddles.Count; i++)
        {
            _paddles[i].Position.Y = MathHelper.Clamp(_paddles[i].Position.Y, minY, maxY);
        }
    }

    private void ResetBallOnPaddle(int ballIndex)
    {
        if ((uint)ballIndex >= (uint)_balls.Count) return;

        _ballServing[ballIndex] = true;
        _balls[ballIndex].Velocity = Vector2.Zero;
        if ((uint)ballIndex < (uint)_ballCaught.Count)
            _ballCaught[ballIndex] = false;

        // Reset launch ramp for this ball.
        if ((uint)ballIndex < (uint)_ballLaunchAgeSeconds.Count)
            _ballLaunchAgeSeconds[ballIndex] = 0f;

        // Reset stored serve offset (default centered).
        EnsureBallListsSized(_balls.Count);
        if ((uint)ballIndex < (uint)_ballServeOffsetX.Count)
            _ballServeOffsetX[ballIndex] = 0f;

        // Attach to the owning paddle if possible.
        int desiredPaddle = Math.Clamp(_balls[ballIndex].OwnerPlayerIndex, 0, _paddles.Count - 1);
        int paddleIndex = Math.Clamp(desiredPaddle, 0, _paddles.Count - 1);
        _balls[ballIndex].Position = _paddles[paddleIndex].Center - new Vector2(0, _balls[ballIndex].Radius + 14);

        // Keep draw color consistent.
        if (_balls[ballIndex].IsExtraBall)
            _balls[ballIndex].DrawColor = Color.White;
        else
            _balls[ballIndex].DrawColor = PlayerBaseColors[Math.Clamp(_balls[ballIndex].OwnerPlayerIndex, 0, PlayerBaseColors.Length - 1)];
    }

    private void ResetBallOnPaddle()
    {
        for (int i = 0; i < _balls.Count; i++)
            ResetBallOnPaddle(i);
    }

    private float GetTargetBallSpeedForLevel()
        => (_preset.BallBaseSpeed + _levelIndex * _preset.SpeedRampPerLevel) * _ballSpeedMultiplier;

    private float GetServeRampMultiplier(int ballIndex)
    {
        if ((uint)ballIndex >= (uint)_ballLaunchAgeSeconds.Count)
            return 1f;

        // If age is 0, we're at start multiplier; once age >= duration, multiplier is 1.
        float t = _ballLaunchAgeSeconds[ballIndex];
        if (t <= 0f) return ServeStartSpeedMultiplier;
        if (t >= ServeRampDurationSeconds) return 1f;

        float u = t / ServeRampDurationSeconds;
        // Smoothstep for a nicer feel.
        u = u * u * (3f - 2f * u);

        return MathHelper.Lerp(ServeStartSpeedMultiplier, 1f, u);
    }

    private void EnsureBallListsSized(int ballCount)
    {
        while (_ballLaunchAgeSeconds.Count < ballCount)
            _ballLaunchAgeSeconds.Add(0f);
        while (_ballLaunchAgeSeconds.Count > ballCount)
            _ballLaunchAgeSeconds.RemoveAt(_ballLaunchAgeSeconds.Count - 1);

        while (_ballServeOffsetX.Count < ballCount)
            _ballServeOffsetX.Add(0f);
        while (_ballServeOffsetX.Count > ballCount)
            _ballServeOffsetX.RemoveAt(_ballServeOffsetX.Count - 1);
    }

    private void Serve(int ballIndex)
    {
        if ((uint)ballIndex >= (uint)_balls.Count) return;

        _ballServing[ballIndex] = false;

        int owner = Math.Clamp(_balls[ballIndex].OwnerPlayerIndex, 0, _paddles.Count - 1);
        int paddleIndex = Math.Clamp(owner, 0, _paddles.Count - 1);

        float targetSpeed = GetTargetBallSpeedForLevel();

        // Blend paddle momentum into the launch direction.
        // Start with "mostly up" but allow a controllable left/right bias.
        float paddleVx = _paddles.Count > 0 ? _paddles[paddleIndex].Velocity.X : 0f;
        float desiredVx = paddleVx * ServePaddleMomentumFactor;

        // Convert desiredVx into an angle and clamp it.
        // We build a direction vector by clamping the angle from vertical.
        float maxAngle = MathHelper.ToRadians(ServeMaxAngleDegrees);
        // Angle from vertical such that sin(angle) = vx / speed.
        float unclamped = targetSpeed > 0f ? (float)Math.Asin(MathHelper.Clamp(desiredVx / targetSpeed, -1f, 1f)) : 0f;
        float angle = MathHelper.Clamp(unclamped, -maxAngle, maxAngle);

        Vector2 dir = new((float)Math.Sin(angle), -(float)Math.Cos(angle));
        if (dir == Vector2.Zero)
            dir = new Vector2(0f, -1f);
        else
            dir = Vector2.Normalize(dir);

        // Start a bit slower, then ramp up smoothly.
        float startSpeed = targetSpeed * ServeStartSpeedMultiplier;
        _balls[ballIndex].Velocity = dir * startSpeed;

        EnsureBallListsSized(_balls.Count);
        _ballLaunchAgeSeconds[ballIndex] = 0f;
    }

    // Replace Update signature to accept multiple players.
    public void Update(GameTime gameTime, DragonBreakInput[] inputs, Viewport vp)
    {
        _totalTime += gameTime.ElapsedGameTime;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        dt = Math.Min(dt, 1f / 20f);

        if (_mode == WorldMode.Menu)
        {
            UpdateMenu(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.Settings)
        {
            UpdateSettingsMenu(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.LevelInterstitial)
        {
            UpdateLevelInterstitial(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.Paused)
        {
            UpdatePaused(inputs, vp, dt);
            return;
        }

        // Gameplay uses the playfield viewport (below the HUD bar).
        var playfield = GetPlayfieldViewport(vp);

        // Allow pausing during gameplay.
        if (AnyPausePressed(inputs))
        {
            EnterPaused();
            return;
        }

        UpdateEffects(dt);

        EnsureBallListsSized(_balls.Count);

        // Clear launch suppression once the player is no longer holding the catch/launch button.
        if (inputs != null)
        {
            for (int p = 0; p < _activePlayerCount && p < inputs.Length && p < _launchSuppressedByPlayer.Count; p++)
            {
                if (_launchSuppressedByPlayer[p] && !inputs[p].CatchHeld)
                    _launchSuppressedByPlayer[p] = false;
            }
        }

        // Update catch armed state.
        // Keyboard originally used press-to-arm; for controller we also want hold-to-catch to work naturally.
        if (inputs != null)
        {
            for (int p = 0; p < _activePlayerCount && p < inputs.Length; p++)
            {
                if (inputs[p].CatchPressed || inputs[p].CatchHeld)
                {
                    _catchArmedByPlayer[p] = true;
                    _catchArmedConsumedByPlayer[p] = false;
                }
            }
        }

        // Update paddles.
        for (int i = 0; i < _paddles.Count; i++)
        {
            var p = _paddles[i];
            p.SpeedPixelsPerSecond = _preset.PaddleSpeed;
            _paddles[i] = p;
        }

        ApplyPaddleSize(playfield);

        float maxY = playfield.Height - PaddleBottomPaddingPixels;
        float minY = Math.Max(0f, playfield.Height * PaddleMaxUpScreenFraction);

        for (int i = 0; i < _paddles.Count; i++)
        {
            float moveX = 0f;
            float moveY = 0f;
            if (inputs != null && i < inputs.Length)
            {
                moveX = inputs[i].MoveX;
                moveY = inputs[i].MoveY;
            }

            _paddles[i].Update(dt, moveX, moveY, playfield.Width, minY, maxY);
        }

        UpdatePowerUps(dt, playfield);

        // Serve handling: each player's serve only launches their own ball(s).
        for (int i = 0; i < _balls.Count; i++)
        {
            if (_ballServing[i])
            {
                int owner = Math.Clamp(_balls[i].OwnerPlayerIndex, 0, _paddles.Count - 1);
                int paddleIndex = Math.Clamp(owner, 0, _paddles.Count - 1);

                // If this ball just entered serving state without an explicit offset recorded,
                // derive it from current X so it doesn't snap to center.
                if ((uint)i < (uint)_ballServeOffsetX.Count)
                {
                    // If the offset is ~0 but the ball is visibly not centered, capture it.
                    float currentOffset = _balls[i].Position.X - _paddles[paddleIndex].Center.X;
                    if (Math.Abs(_ballServeOffsetX[i]) < 0.001f && Math.Abs(currentOffset) > 0.5f)
                        _ballServeOffsetX[i] = currentOffset;
                }

                float offsetX = (uint)i < (uint)_ballServeOffsetX.Count ? _ballServeOffsetX[i] : 0f;

                float minOffset = -(_paddles[paddleIndex].Size.X * 0.5f) + _balls[i].Radius;
                float maxOffset = (_paddles[paddleIndex].Size.X * 0.5f) - _balls[i].Radius;
                offsetX = MathHelper.Clamp(offsetX, minOffset, maxOffset);

                if ((uint)i < (uint)_ballServeOffsetX.Count)
                    _ballServeOffsetX[i] = offsetX;

                _balls[i].Position = new Vector2(
                    _paddles[paddleIndex].Center.X + offsetX,
                    _paddles[paddleIndex].Bounds.Top - _balls[i].Radius - 0.5f);

                bool catchReleased = false;
                bool servePressed = false;
                if (inputs != null && (uint)owner < (uint)inputs.Length)
                {
                    // While suppressed, ignore release events caused by exiting menus.
                    bool suppressed = owner < _launchSuppressedByPlayer.Count && _launchSuppressedByPlayer[owner];

                    catchReleased = !suppressed && inputs[owner].CatchReleased;
                    servePressed = !suppressed && inputs[owner].ServePressed;
                }

                bool isPrimary = owner < _primaryBallIndexByPlayer.Count && _primaryBallIndexByPlayer[owner] == i;

                // Launch handling:
                // - Primary (non-extra) balls keep the "caught" safety rules.
                // - Extra balls can also be launched if they ever end up attached to the paddle.
                bool launchRequested;
                if (_balls[i].IsExtraBall)
                {
                    // Extra balls: allow launch on either serve press or catch release.
                    launchRequested = servePressed || catchReleased;
                }
                else if (isPrimary)
                {
                    // Primary ball: do NOT auto-launch at start.
                    // Only launch on explicit serve input or catch release.
                    launchRequested = servePressed || catchReleased;
                }
                else
                {
                    // Non-primary, non-extra balls shouldn't normally be in serving state; don't auto-launch.
                    launchRequested = false;
                }

                if (launchRequested)
                {
                    Serve(i);

                    if (i < _ballCaught.Count)
                        _ballCaught[i] = false;

                    if (owner < _catchArmedByPlayer.Count)
                    {
                        _catchArmedByPlayer[owner] = false;
                        _catchArmedConsumedByPlayer[owner] = false;
                    }
                }

                continue;
            }

            // Age since last launch for ramping speed.
            if ((uint)i < (uint)_ballLaunchAgeSeconds.Count && _ballLaunchAgeSeconds[i] < ServeRampDurationSeconds)
                _ballLaunchAgeSeconds[i] += dt;

            // Apply post-launch speed ramp without changing direction.
            float rampMul = GetServeRampMultiplier(i);
            float desiredSpeed = GetTargetBallSpeedForLevel() * rampMul;

            // Only normalize if the ball has non-zero velocity.
            float len = _balls[i].Velocity.Length();
            if (len > 0.001f)
                _balls[i].Velocity = _balls[i].Velocity * (desiredSpeed / len);

            _balls[i].Update(dt);

            HandleWallCollisions(playfield, _balls[i]);
            HandlePaddleCollision(i, inputs, playfield);
            HandleBrickCollisions(_balls[i]);

            // Ball lost (bottom of playfield)
            if (_balls[i].Position.Y - _balls[i].Radius > playfield.Height)
            {
                OnBallLost(i, playfield);
                return; // mode may change; safe early-exit
            }
        }

        // Level cleared
        if (IsOwnedBricksMode)
        {
            // Must clear all owned bricks (for all players) to advance.
            bool anyOwnedAlive = false;
            for (int i = 0; i < _bricks.Count; i++)
            {
                if (_bricks[i].IsAlive)
                {
                    anyOwnedAlive = true;
                    break;
                }
            }

            if (!anyOwnedAlive)
                OnLevelCleared(vp);
        }
        else
        {
            bool anyAlive = false;
            for (int i = 0; i < _bricks.Count; i++)
            {
                if (_bricks[i].IsAlive)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
                OnLevelCleared(vp);
        }
    }

    private void UpdatePowerUps(float dt, Viewport vp)
    {
        for (int i = _powerUps.Count - 1; i >= 0; i--)
        {
            var p = _powerUps[i];
            if (!p.IsAlive)
            {
                _powerUps.RemoveAt(i);
                continue;
            }

            p.Update(dt);

            for (int pi = 0; pi < _paddles.Count; pi++)
            {
                if (p.Bounds.Intersects(_paddles[pi].Bounds))
                {
                    ApplyPowerUp(p.Type);
                    ShowToast(GetPowerUpToastText(p.Type), ToastDurationSeconds);
                    p.IsAlive = false;
                    break;
                }
            }

            if (!p.IsAlive)
            {
                _powerUps.RemoveAt(i);
                continue;
            }

            if (p.Position.Y - p.Size.Y > vp.Height + 40)
                _powerUps.RemoveAt(i);
        }
    }

    private static bool AnyPausePressed(DragonBreakInput[] inputs)
    {
        if (inputs == null) return false;
        for (int i = 0; i < inputs.Length; i++)
        {
            // PausePressed is P/Start; MenuBackPressed covers Escape/Back
            if (inputs[i].PausePressed || inputs[i].MenuBackPressed)
                return true;
        }

        return false;
    }

    private void EnterPaused()
    {
        _pauseMenu.ResetSelection();
        _mode = WorldMode.Paused;

        // Prevent an immediate serve/catch release when resuming.
        for (int p = 0; p < _launchSuppressedByPlayer.Count; p++)
            _launchSuppressedByPlayer[p] = true;
    }

    private void ExitPausedToPlaying()
    {
        _mode = WorldMode.Playing;

        // Same suppression when leaving other menus.
        for (int p = 0; p < _launchSuppressedByPlayer.Count; p++)
            _launchSuppressedByPlayer[p] = true;
    }

    private void UpdatePaused(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        // While paused, only tick toast so UI messages can fade.
        if (_toastTimeLeft > 0f)
        {
            _toastTimeLeft -= dt;
            if (_toastTimeLeft <= 0f)
            {
                _toastTimeLeft = 0f;
                _toastText = string.Empty;
            }
        }

        _pauseMenu.Update(inputs, vp, dt);

        switch (_pauseMenu.ConsumeAction())
        {
            case PauseMenuScreen.PauseAction.Resume:
                ExitPausedToPlaying();
                break;

            case PauseMenuScreen.PauseAction.RestartLevel:
            {
                var playfield = GetPlayfieldViewport(vp);
                _powerUps.Clear();
                ClearEffects();
                LoadLevel(playfield, _levelIndex);
                ResetBallOnPaddle();
                ExitPausedToPlaying();
                break;
            }

            case PauseMenuScreen.PauseAction.MainMenu:
                _mode = WorldMode.Menu;
                _menuItem = MenuItem.Start;
                break;
        }
    }

    private void TrySpawnPowerUp(Rectangle brickBounds)
    {
        // Read chance from settings (configurable), falling back to defaults.
        var gameplay = _settings?.Current.Gameplay ?? GameplaySettings.Default;
        float chance = gameplay.GetPowerUpDropChance(_selectedDifficultyId);
        chance = MathHelper.Clamp(chance, 0f, 1f);

        if (_rng.NextDouble() > chance)
            return;

        double roll = _rng.NextDouble();

        // Casual has infinite balls; extra lives are meaningless, so swap it for an instant reward.
        bool casual = IsCasualNoLose;

        PowerUpType type = roll switch
        {
            < 0.24 => PowerUpType.ExpandPaddle,
            < 0.43 => PowerUpType.SlowBall,
            < 0.58 => PowerUpType.ScoreBoost,
            < 0.73 => PowerUpType.FastBall,
            < 0.88 => PowerUpType.MultiBall,
            _ => casual ? PowerUpType.ScoreBurst : PowerUpType.ExtraLife,
        };

        var pos = new Vector2(brickBounds.Center.X, brickBounds.Center.Y);
        _powerUps.Add(new PowerUp(type, pos));
    }

    private void ApplyPowerUp(PowerUpType type)
    {
        switch (type)
        {
            case PowerUpType.ExpandPaddle:
                _paddleWidthMultiplier = 1.35f;
                _paddleWidthTimeLeft = Math.Max(_paddleWidthTimeLeft, 12f);
                break;

            case PowerUpType.SlowBall:
                _ballSpeedMultiplier = 0.82f;
                _ballSpeedTimeLeft = Math.Max(_ballSpeedTimeLeft, 10f);
                // Normalize ball speeds down to the target for the current level.
                for (int i = 0; i < _balls.Count; i++)
                {
                    if (_balls[i].Velocity != Vector2.Zero)
                        _balls[i].Velocity = Vector2.Normalize(_balls[i].Velocity) * GetTargetBallSpeedForLevel();
                }
                break;

            case PowerUpType.FastBall:
                _ballSpeedMultiplier = 1.20f;
                _ballSpeedTimeLeft = Math.Max(_ballSpeedTimeLeft, 10f);
                for (int i = 0; i < _balls.Count; i++)
                {
                    if (_balls[i].Velocity != Vector2.Zero)
                        _balls[i].Velocity = Vector2.Normalize(_balls[i].Velocity) * GetTargetBallSpeedForLevel();
                }
                break;

            case PowerUpType.ScoreBoost:
                _scoreMultiplier = 2f;
                _scoreMultiplierTimeLeft = Math.Max(_scoreMultiplierTimeLeft, 12f);
                break;

            case PowerUpType.MultiBall:
                // Spawn one extra ball per player, white, and it never costs lives when lost.
                // These should launch immediately (not attach to paddle), so multiball feels snappy.
                for (int pi = 0; pi < _paddles.Count; pi++)
                {
                    var newBall = new Ball(_paddles[pi].Center - new Vector2(0, 18), radius: 8f, ownerPlayerIndex: pi, isExtraBall: true)
                    {
                        DrawColor = Color.White,
                    };

                    _balls.Add(newBall);
                    _ballServing.Add(false);
                    _ballCaught.Add(false);

                    // Give it an immediate serve with a slight per-player horizontal spread.
                    int newIndex = _balls.Count - 1;

                    float baseAngleDeg = -90f;
                    float spread = _paddles.Count <= 1 ? 0f : MathHelper.Lerp(-15f, 15f, pi / (float)Math.Max(1, _paddles.Count - 1));
                    float angle = MathHelper.ToRadians(baseAngleDeg + spread + _rng.Next(-8, 9));

                    float speed = GetTargetBallSpeedForLevel();
                    var v = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;

                    if (Math.Abs(v.X) < 80f)
                        v.X = Math.Sign(v.X == 0 ? 1 : v.X) * 80f;

                    _balls[newIndex].Velocity = v;
                }
                break;

            case PowerUpType.ExtraLife:
                // Give +1 life to all players still in the game (positive-only).
                if (!IsCasualNoLose)
                {
                    for (int i = 0; i < _livesByPlayer.Count; i++)
                        _livesByPlayer[i]++;
                }
                break;

            case PowerUpType.ScoreBurst:
                // Instant score, scaled by current multiplier.
                // Award goes to all active players so it feels good in co-op.
                int basePoints = 250;
                int pts = (int)MathF.Round(basePoints * _scoreMultiplier);
                for (int i = 0; i < _scoreByPlayer.Count; i++)
                    _scoreByPlayer[i] += pts;
                break;
        }
    }

    private float GetEffectTimeLeft()
    {
        // Return the minimum active time left of any effect (paddle/ball speed, score multiplier).
        // Used to decide if we should show the effects row in the HUD.
        float min = float.MaxValue;

        if (_paddleWidthTimeLeft > 0.01f)
            min = Math.Min(min, _paddleWidthTimeLeft);

        if (_ballSpeedTimeLeft > 0.01f)
            min = Math.Min(min, _ballSpeedTimeLeft);

        if (_scoreMultiplierTimeLeft > 0.01f)
            min = Math.Min(min, _scoreMultiplierTimeLeft);

        return min;
    }

    private void ShowToast(string text, float durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _toastText = text;
        _toastTimeLeft = Math.Max(_toastTimeLeft, durationSeconds);
    }

    private static string GetPowerUpToastText(PowerUpType type)
        => type switch
        {
            PowerUpType.ExpandPaddle => "Paddle expanded",
            PowerUpType.SlowBall => "Ball slowed",
            PowerUpType.FastBall => "Ball sped up",
            PowerUpType.ExtraLife => "+1 Life",
            PowerUpType.ScoreBoost => "Score x2",
            PowerUpType.MultiBall => "Multiball",
            PowerUpType.ScoreBurst => "+Score",
            _ => type.ToString(),
        };

    private void DrawCenteredText(SpriteBatch sb, Viewport vp, string text, float y, Color color)
    {
        if (_hudFont == null)
            return;

        var ui = _settings?.Current.Ui ?? UiSettings.Default;
        float scale = ui.HudScale;

        var s = SafeText(text);
        var size = _hudFont.MeasureString(s) * scale;
        float x = (vp.Width - size.X) * 0.5f;
        sb.DrawString(_hudFont, s, new Vector2(Math.Max(12, x), y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawMenu(SpriteBatch sb, Viewport vp)
    {
        if (_hudFont == null)
            return;

        DrawCenteredText(sb, vp, "DRAGONBREAK", 70, Color.White);

        float startY = 150;
        float lineH = 34;

        string sel(MenuItem item, string label, string value)
        {
            bool selected = _menuItem == item;
            string prefix = selected ? ">" : " ";
            // Keep per-line readable; align-ish with separator.
            return $"{prefix} {label}: {value}";
        }

        var lines = new List<(string Text, Color Color)>
        {
            (sel(MenuItem.GameMode, "Mode", _selectedGameMode.ToString()), _menuItem == MenuItem.GameMode ? Color.White : new Color(210, 210, 220)),
            (sel(MenuItem.Players, "Players", _selectedPlayers.ToString()), _menuItem == MenuItem.Players ? Color.White : new Color(210, 210, 220)),
            (sel(MenuItem.Difficulty, "Difficulty", DifficultyLabel(_selectedPresetIndex)), _menuItem == MenuItem.Difficulty ? Color.White : new Color(210, 210, 220)),
            (sel(MenuItem.Settings, "Settings", "Open"), _menuItem == MenuItem.Settings ? Color.White : new Color(210, 210, 220)),
            (sel(MenuItem.Start, "Start", "Go"), _menuItem == MenuItem.Start ? Color.White : new Color(210, 210, 220)),
        };

        for (int i = 0; i < lines.Count; i++)
        {
            DrawCenteredText(sb, vp, lines[i].Text, startY + i * lineH, lines[i].Color);
        }

        DrawCenteredText(sb, vp, "Up/Down selects. Left/Right changes values. Confirm selects.", vp.Height - 90, new Color(180, 180, 190));
        DrawCenteredText(sb, vp, "Back opens Settings. Esc exits.", vp.Height - 60, new Color(180, 180, 190));
    }

    private void DrawSettings(SpriteBatch sb, Viewport vp)
    {
        if (_hudFont == null)
            return;

        var pending = _settings?.Pending;
        if (pending == null)
        {
            DrawCenteredText(sb, vp, "Settings unavailable", 120, Color.White);
            return;
        }

        DrawCenteredText(sb, vp, "SETTINGS", 60, Color.White);

        string sel(SettingsItem item, string label) => _settingsItem == item ? $"> {label}" : $"  {label}";

        float startY = 120;
        float lineH = 28;

        var display = pending.Display;
        var audio = pending.Audio;
        var gameplay = pending.Gameplay;
        var ui = pending.Ui;

        var lines = new List<(SettingsItem Item, string Text)>
        {
            (SettingsItem.WindowMode, sel(SettingsItem.WindowMode, $"Window Mode: {display.WindowMode}")),
            (SettingsItem.Resolution, sel(SettingsItem.Resolution, $"Resolution: {display.Width}x{display.Height}")),
            (SettingsItem.VSync, sel(SettingsItem.VSync, $"VSync: {(display.VSync ? "On" : "Off")}")),

            (SettingsItem.MasterVolume, sel(SettingsItem.MasterVolume, $"Master Volume: {(int)(audio.MasterVolume * 100)}%")),
            (SettingsItem.BgmVolume, sel(SettingsItem.BgmVolume, $"BGM Volume: {(int)(audio.BgmVolume * 100)}%")),
            (SettingsItem.SfxVolume, sel(SettingsItem.SfxVolume, $"SFX Volume: {(int)(audio.SfxVolume * 100)}%")),

            (SettingsItem.HudEnabled, sel(SettingsItem.HudEnabled, $"HUD: {(ui.ShowHud ? "On" : "Off")}")),
            (SettingsItem.HudScale, sel(SettingsItem.HudScale, $"HUD Scale: {ui.HudScale:0.00}x")),
            (SettingsItem.HudP1, sel(SettingsItem.HudP1, $"Show P1 HUD: {(ui.ShowP1Hud ? "On" : "Off")}")),
            (SettingsItem.HudP2, sel(SettingsItem.HudP2, $"Show P2 HUD: {(ui.ShowP2Hud ? "On" : "Off")}")),
            (SettingsItem.HudP3, sel(SettingsItem.HudP3, $"Show P3 HUD: {(ui.ShowP3Hud ? "On" : "Off")}")),
            (SettingsItem.HudP4, sel(SettingsItem.HudP4, $"Show P4 HUD: {(ui.ShowP4Hud ? "On" : "Off")}")),

            (SettingsItem.ContinueMode, sel(SettingsItem.ContinueMode, $"Continue Mode: {ContinueModeLabel(gameplay.ContinueMode)}")),
            (SettingsItem.AutoContinueSeconds, sel(SettingsItem.AutoContinueSeconds, $"Auto Continue: {gameplay.AutoContinueSeconds:0.0}s")),

            (SettingsItem.LevelSeed, sel(SettingsItem.LevelSeed, $"Level Seed: {gameplay.LevelSeed}")),
            (SettingsItem.LevelSeedRandomize, sel(SettingsItem.LevelSeedRandomize, "Seed: Randomize")),
            (SettingsItem.LevelSeedReset, sel(SettingsItem.LevelSeedReset, "Seed: Reset (1337)")),

            (SettingsItem.Apply, sel(SettingsItem.Apply, "APPLY")),
            (SettingsItem.Cancel, sel(SettingsItem.Cancel, "CANCEL")),
        };

        for (int i = 0; i < lines.Count; i++)
        {
            Color c = _settingsItem == lines[i].Item ? Color.White : new Color(210, 210, 220);
            DrawCenteredText(sb, vp, lines[i].Text, startY + i * lineH, c);
        }

        DrawCenteredText(sb, vp, "Up/Down select. Left/Right change. Confirm = activate. Back = cancel.", vp.Height - 60, new Color(180, 180, 190));
    }

    private void DrawHud(SpriteBatch sb, Viewport vp)
    {
        if (_hudFont == null)
            return;

        var ui = _settings?.Current.Ui ?? UiSettings.Default;
        if (!ui.ShowHud)
            return;

        float scale = ui.HudScale;

        // Hard UI bar at the top that is NOT part of the playfield.
        // This replaces the old translucent overlay behavior.
        int hudBarH = GetHudBarHeight(vp);
        if (hudBarH > 0)
            sb.Draw(_pixel, new Rectangle(0, 0, vp.Width, hudBarH), Color.Black);

        // Keep HUD text inside the bar.
        float topPadding = 8f;
        float lineY1 = topPadding;
        float lineY2 = topPadding + (_hudFont.LineSpacing * scale);
        float lineY3 = topPadding + (_hudFont.LineSpacing * scale) * 2f;

        // Level + difficulty + mode title in top center.
        string diff = DifficultyLabel(_selectedPresetIndex);
        string top = $"LEVEL {_levelIndex + 1}   DIFF {diff}   MODE {_selectedGameMode}";
        var topSafe = SafeText(top);
        var topSize = _hudFont.MeasureString(topSafe) * scale;
        sb.DrawString(_hudFont, topSafe, new Vector2((vp.Width - topSize.X) * 0.5f, lineY1), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

        void DrawPlayerPanel(int player, Vector2 anchor, bool rightAlign)
        {
            if (player < 0 || player >= _activePlayerCount)
                return;
            bool show = player switch
            {
                0 => ui.ShowP1Hud,
                1 => ui.ShowP2Hud,
                2 => ui.ShowP3Hud,
                3 => ui.ShowP4Hud,
                _ => true,
            };

            if (!show) return;

            int score = player < _scoreByPlayer.Count ? _scoreByPlayer[player] : 0;
            string balls = IsCasualNoLose ? "∞" : (player < _livesByPlayer.Count ? _livesByPlayer[player].ToString() : "0");

            string text = $"P{player + 1}  SCORE {score}  BALLS {balls}";
            string safe = SafeText(text);
            var size = _hudFont.MeasureString(safe) * scale;

            Vector2 pos = rightAlign
                ? new Vector2(anchor.X - size.X, anchor.Y)
                : anchor;

            var color = PlayerBaseColors[Math.Clamp(player, 0, PlayerBaseColors.Length - 1)];
            sb.DrawString(_hudFont, safe, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        // Put all player panels in the top bar.
        DrawPlayerPanel(0, new Vector2(12, lineY2), rightAlign: false);
        DrawPlayerPanel(1, new Vector2(vp.Width - 12, lineY2), rightAlign: true);

        // If 3–4 players, place them on the third line inside the bar.
        DrawPlayerPanel(2, new Vector2(12, lineY3), rightAlign: false);
        DrawPlayerPanel(3, new Vector2(vp.Width - 12, lineY3), rightAlign: true);

        // Interstitial prompt line: keep it in the HUD bar if possible; otherwise fall back to bottom.
        if (_mode == WorldMode.LevelInterstitial)
        {
            string prompt = (_settings?.Current.Gameplay.ContinueMode ?? ContinueMode.PromptThenAuto) == ContinueMode.Prompt
                ? "Continue or nah? (Confirm)"
                : "Continue or nah? (Confirm)  |  auto...";

            string line = $"{prompt}   {_levelInterstitialLine}";
            string safeLine = SafeText(line);

            float y = lineY3 + (_hudFont.LineSpacing * scale);
            bool fitsTop = y + (_hudFont.LineSpacing * scale) <= hudBarH;

            if (fitsTop)
            {
                sb.DrawString(_hudFont, safeLine, new Vector2(12, y), _levelInterstitialWasWin ? new Color(120, 235, 120) : new Color(255, 120, 80), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            else
            {
                sb.DrawString(_hudFont, safeLine, new Vector2(12, vp.Height - (12 + _hudFont.LineSpacing * scale)), _levelInterstitialWasWin ? new Color(120, 235, 120) : new Color(255, 120, 80), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        // Active effect timers (only show while playing/paused).
        if (_mode == WorldMode.Playing || _mode == WorldMode.Paused)
        {
            var effects = new List<string>(3);

            if (_paddleWidthTimeLeft > 0.01f)
                effects.Add($"WIDE {_paddleWidthTimeLeft:0.0}s");

            if (_ballSpeedTimeLeft > 0.01f)
            {
                string label = _ballSpeedMultiplier >= 1.01f ? "FAST" : "SLOW";
                effects.Add($"{label} {_ballSpeedTimeLeft:0.0}s");
            }

            if (_scoreMultiplierTimeLeft > 0.01f)
                effects.Add($"x{_scoreMultiplier:0.#} {_scoreMultiplierTimeLeft:0.0}s");

            if (effects.Count > 0)
            {
                string text = string.Join("  |  ", effects);
                string safe = SafeText(text);

                var size = _hudFont.MeasureString(safe) * scale;
                sb.DrawString(_hudFont, safe, new Vector2(vp.Width - 12 - size.X, lineY1), new Color(210, 210, 220), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // Background
        sb.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), new Color(16, 16, 20));

        // Only draw the gameplay canvas while actually in-game.
        // This prevents stale gameplay visuals showing behind the Menu/Settings/Interstitial screens.
        if (_mode != WorldMode.Playing && _mode != WorldMode.Paused)
            return;

        // Translate gameplay drawing down so (0,0) in game space maps to the top of the PLAYFIELD,
        // not the top of the window (which is reserved for the HUD bar).
        int hudH = GetHudBarHeight(vp);

        // Bricks
        for (int i = 0; i < _bricks.Count; i++)
        {
            var b = _bricks[i];
            if (!b.IsAlive) continue;

            Color c = Color.White;
            try
            {
                if (b.Palette != null && b.Palette.Length > 0)
                {
                    int hpIndex = Math.Clamp(b.HitPoints - 1, 0, b.Palette.Length - 1);
                    c = b.Palette[hpIndex];
                }
            }
            catch
            {
                c = Color.White;
            }

            var r = b.Bounds;
            r.Y += hudH;
            sb.Draw(_pixel, r, c);
        }

        // Paddles
        for (int i = 0; i < _paddles.Count; i++)
        {
            var r = _paddles[i].Bounds;
            r.Y += hudH;
            sb.Draw(_pixel, r, PlayerBaseColors[Math.Clamp(i, 0, PlayerBaseColors.Length - 1)]);
        }

        // Balls
        for (int i = 0; i < _balls.Count; i++)
        {
            var ball = _balls[i];
            var r = ball.Bounds;
            r.Y += hudH;

            var c = ball.DrawColor ?? (ball.IsExtraBall ? Color.White : PlayerBaseColors[Math.Clamp(ball.OwnerPlayerIndex, 0, PlayerBaseColors.Length - 1)]);
            sb.Draw(_pixel, r, c);
        }

        // PowerUps
        for (int i = 0; i < _powerUps.Count; i++)
        {
            var r = _powerUps[i].Bounds;
            r.Y += hudH;
            sb.Draw(_pixel, r, Color.Gold);
        }
    }

    /// <summary>
    /// Draws screen-space UI (menus/settings/HUD/top bar). Call this WITHOUT scissor.
    /// </summary>
    public void DrawUi(SpriteBatch sb, Viewport vp)
    {
        // For now, if we're in menu/settings we only show those screens.
        if (_mode == WorldMode.Menu)
        {
            DrawMenu(sb, vp);
            return;
        }

        if (_mode == WorldMode.Settings)
        {
            DrawSettings(sb, vp);
            return;
        }

        // In-game HUD + top bar.
        DrawHud(sb, vp);

        if (_mode == WorldMode.Paused)
        {
            DrawPauseOverlay(sb, vp);
        }

        // Toast (screen-space) anchored to playfield bottom-right.
        if (_hudFont != null && _toastTimeLeft > 0f && !string.IsNullOrWhiteSpace(_toastText))
        {
            var playfield = GetPlayfieldViewport(vp);

            var ui = _settings?.Current.Ui ?? UiSettings.Default;
            float scale = MathHelper.Clamp(ui.HudScale, 0.5f, 2.5f);

            Vector2 size = _hudFont.MeasureString(_toastText) * scale;

            const float margin = 12f;
            float x = playfield.X + playfield.Width - margin - size.X;
            float y = playfield.Y + playfield.Height - margin - size.Y;

            float a = MathHelper.Clamp(_toastTimeLeft / ToastDurationSeconds, 0f, 1f);
            var col = Color.White * a;
            var shadow = Color.Black * (0.65f * a);

            sb.DrawString(_hudFont, _toastText, new Vector2(x + 1, y + 1), shadow, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_hudFont, _toastText, new Vector2(x, y), col, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }

    private void DrawPauseOverlay(SpriteBatch sb, Viewport vp)
    {
        if (_hudFont == null)
            return;

        // Darken screen a bit
        sb.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.55f);

        DrawCenteredText(sb, vp, "PAUSED", 120, Color.White);

        float y = 185;
        float lineH = 34;
        foreach (var (label, selected) in _pauseMenu.GetLines())
        {
            string prefix = selected ? ">" : " ";
            DrawCenteredText(sb, vp, $"{prefix} {label}", y, selected ? Color.White : new Color(210, 210, 220));
            y += lineH;
        }

        DrawCenteredText(sb, vp, "Confirm selects. Back resumes.", vp.Height - 60, new Color(180, 180, 190));
    }

    // --- Missing methods reintroduced (Update/Draw dependencies) ---

    private string DifficultyLabel(int presetIndex)
    {
        presetIndex = Math.Clamp(presetIndex, 0, Presets.Length - 1);
        return Presets[presetIndex].Name;
    }

    private string ContinueModeLabel(ContinueMode mode)
        => mode switch
        {
            ContinueMode.Auto => "auto",
            ContinueMode.Prompt => "prompt",
            ContinueMode.PromptThenAuto => "prompt+auto",
            _ => mode.ToString(),
        };

    private static int DifficultyToPresetIndex(DifficultyId id)
        => id switch
        {
            DifficultyId.Casual => 0,
            DifficultyId.VeryEasy => 1,
            DifficultyId.Easy => 2,
            DifficultyId.Normal => 3,
            DifficultyId.Hard => 4,
            DifficultyId.VeryHard => 5,
            DifficultyId.Extreme => 6,
            _ => 3,
        };

    private static DifficultyId PresetIndexToDifficulty(int presetIndex)
        => presetIndex switch
        {
            0 => DifficultyId.Casual,
            1 => DifficultyId.VeryEasy,
            2 => DifficultyId.Easy,
            3 => DifficultyId.Normal,
            4 => DifficultyId.Hard,
            5 => DifficultyId.VeryHard,
            6 => DifficultyId.Extreme,
            _ => DifficultyId.Normal,
        };

    private static float GetDifficultyPaddleWidthMultiplier(DifficultyId id)
        => id switch
        {
            DifficultyId.Normal => 1.00f,
            DifficultyId.Casual => 1.5f,
            DifficultyId.VeryEasy => 1.25f,
            DifficultyId.Easy => 0.75f,
            DifficultyId.Hard => 0.5f,
            DifficultyId.VeryHard => 0.40f,
            DifficultyId.Extreme => 0.25f,
            _ => 1.00f,
        };

    private void SyncSelectionsFromSettings()
    {
        if (_settings == null) return;

        var s = _settings.Current;
        _selectedDifficultyId = s.Gameplay.Difficulty;
        _selectedPresetIndex = Math.Clamp(DifficultyToPresetIndex(_selectedDifficultyId), 0, Presets.Length - 1);
        _preset = Presets[_selectedPresetIndex];
        _difficultyPaddleWidthMultiplier = GetDifficultyPaddleWidthMultiplier(_selectedDifficultyId);
    }

    private void UpdateMenu(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        bool confirmPressed = false, backPressed = false;

        float menuX = 0f;
        float menuY = 0f;

        bool upHeldAny = false;
        bool downHeldAny = false;
        bool leftHeldAny = false;
        bool rightHeldAny = false;

        if (inputs != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
                backPressed |= inputs[i].MenuBackPressed;

                upHeldAny |= inputs[i].MenuUpHeld;
                downHeldAny |= inputs[i].MenuDownHeld;
                leftHeldAny |= inputs[i].MenuLeftHeld;
                rightHeldAny |= inputs[i].MenuRightHeld;

                if (Math.Abs(inputs[i].MenuMoveX) > Math.Abs(menuX)) menuX = inputs[i].MenuMoveX;
                if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(menuY)) menuY = inputs[i].MenuMoveY;
            }
        }

        bool upHeld = upHeldAny || menuY >= MenuAxisDeadzone;
        bool downHeld = downHeldAny || menuY <= -MenuAxisDeadzone;
        bool leftHeld = leftHeldAny || menuX <= -MenuAxisDeadzone;
        bool rightHeld = rightHeldAny || menuX >= MenuAxisDeadzone;

        if (upHeld && !_menuUpConsumed)
        {
            int count = Enum.GetValues<MenuItem>().Length;
            _menuItem = (MenuItem)(((int)_menuItem - 1 + count) % count);
            _menuUpConsumed = true;
        }
        if (!upHeld) _menuUpConsumed = false;

        if (downHeld && !_menuDownConsumed)
        {
            int count = Enum.GetValues<MenuItem>().Length;
            _menuItem = (MenuItem)(((int)_menuItem + 1) % count);
            _menuDownConsumed = true;
        }
        if (!downHeld) _menuDownConsumed = false;

        if (leftHeld && !_menuLeftConsumed)
        {
            AdjustMenuValue(-1);
            _menuLeftConsumed = true;
        }
        if (!leftHeld) _menuLeftConsumed = false;

        if (rightHeld && !_menuRightConsumed)
        {
            AdjustMenuValue(+1);
            _menuRightConsumed = true;
        }
        if (!rightHeld) _menuRightConsumed = false;

        if (confirmPressed)
        {
            if (_menuItem == MenuItem.Start)
            {
                _activePlayerCount = _selectedPlayers;

                _selectedPresetIndex = Math.Clamp(_selectedPresetIndex, 0, Presets.Length - 1);
                _selectedDifficultyId = PresetIndexToDifficulty(_selectedPresetIndex);
                _difficultyPaddleWidthMultiplier = GetDifficultyPaddleWidthMultiplier(_selectedDifficultyId);

                if (_settings != null)
                {
                    var current = _settings.Current;
                    _settings.UpdateCurrent(current with { Gameplay = current.Gameplay with { Difficulty = _selectedDifficultyId } }, save: true);
                }

                StartNewGame(vp, Presets[_selectedPresetIndex]);
                _mode = WorldMode.Playing;
            }
            else if (_menuItem == MenuItem.Settings)
            {
                OpenSettings();
            }
            else
            {
                _menuItem = MenuItem.Start;
            }
        }

        if (backPressed)
        {
            _selectedPresetIndex = 3;
            _selectedPlayers = 1;
            _selectedGameMode = GameMode.Arcade;
            _menuItem = MenuItem.Start;
        }

        if (inputs != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].MenuBackPressed)
                {
                    OpenSettings();
                    break;
                }
            }
        }
    }

    private void AdjustMenuValue(int dir)
    {
        switch (_menuItem)
        {
            case MenuItem.GameMode:
            {
                int count = Enum.GetValues<GameMode>().Length;
                _selectedGameMode = (GameMode)((((int)_selectedGameMode + dir) % count + count) % count);
                break;
            }
            case MenuItem.Players:
                _selectedPlayers = Math.Clamp(_selectedPlayers + dir, 1, 4);
                break;
            case MenuItem.Difficulty:
                _selectedPresetIndex = Math.Clamp(_selectedPresetIndex + dir, 0, Presets.Length - 1);
                break;
        }
    }

    private void OpenSettings()
    {
        if (_settings == null)
            return;

        _settings.BeginEdit();

        var pending = _settings.Pending ?? _settings.Current;
        EnsureResolutionIndex(pending.Display.Width, pending.Display.Height);

        _settingsItem = SettingsItem.WindowMode;
        _mode = WorldMode.Settings;
    }

    private void EnsureResolutionIndex(int w, int h)
    {
        if (_resolutions == null || _resolutions.Count == 0)
        {
            _resolutionIndex = 0;
            return;
        }

        int best = 0;
        int bestScore = int.MaxValue;
        for (int i = 0; i < _resolutions.Count; i++)
        {
            int dx = _resolutions[i].Width - w;
            int dy = _resolutions[i].Height - h;
            int score = dx * dx + dy * dy;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        _resolutionIndex = best;
    }

    private void UpdateSettingsMenu(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        if (_settings == null || _settings.Pending == null)
        {
            _mode = WorldMode.Menu;
            return;
        }

        bool confirmPressed = false;
        bool backPressed = false;
        bool upPressed = false;
        bool downPressed = false;
        bool leftHeld = false;
        bool rightHeld = false;

        if (inputs != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
                backPressed |= inputs[i].MenuBackPressed;
                upPressed |= inputs[i].MenuUpPressed;
                downPressed |= inputs[i].MenuDownPressed;
                leftHeld |= inputs[i].MenuLeftHeld;
                rightHeld |= inputs[i].MenuRightHeld;
            }
        }

        if (upPressed)
        {
            int count = Enum.GetValues<SettingsItem>().Length;
            _settingsItem = (SettingsItem)(((int)_settingsItem - 1 + count) % count);
        }

        if (downPressed)
        {
            int count = Enum.GetValues<SettingsItem>().Length;
            _settingsItem = (SettingsItem)(((int)_settingsItem + 1) % count);
        }

        int dir = 0;

        // Debounce: treat held left/right as single 'click' events.
        if (leftHeld && !_settingsLeftConsumed)
        {
            dir -= 1;
            _settingsLeftConsumed = true;
        }
        if (!leftHeld)
            _settingsLeftConsumed = false;

        if (rightHeld && !_settingsRightConsumed)
        {
            dir += 1;
            _settingsRightConsumed = true;
        }
        if (!rightHeld)
            _settingsRightConsumed = false;

        if (dir != 0)
            AdjustSettingsValue(dir);

        if (confirmPressed)
        {
            if (_settingsItem == SettingsItem.Apply)
            {
                _settings.ApplyPending();
                SyncSelectionsFromSettings();
                _mode = WorldMode.Menu;
                return;
            }

            if (_settingsItem == SettingsItem.Cancel)
            {
                _settings.CancelEdit();
                _mode = WorldMode.Menu;
                return;
            }

            // toggle items
            var pending = _settings.Pending;
            if (pending != null)
            {
                var display = pending.Display;
                var audio = pending.Audio;
                var gameplay = pending.Gameplay;
                var ui = pending.Ui;

                switch (_settingsItem)
                {
                    case SettingsItem.VSync:
                        display = display with { VSync = !display.VSync };
                        break;
                    case SettingsItem.HudEnabled:
                        ui = ui with { ShowHud = !ui.ShowHud };
                        break;
                    case SettingsItem.HudP1:
                        ui = ui with { ShowP1Hud = !ui.ShowP1Hud };
                        break;
                    case SettingsItem.HudP2:
                        ui = ui with { ShowP2Hud = !ui.ShowP2Hud };
                        break;
                    case SettingsItem.HudP3:
                        ui = ui with { ShowP3Hud = !ui.ShowP3Hud };
                        break;
                    case SettingsItem.HudP4:
                        ui = ui with { ShowP4Hud = !ui.ShowP4Hud };
                        break;
                    case SettingsItem.LevelSeedRandomize:
                        gameplay = gameplay with { LevelSeed = Random.Shared.Next(1, int.MaxValue) };
                        break;
                    case SettingsItem.LevelSeedReset:
                        gameplay = gameplay with { LevelSeed = 1337 };
                        break;
                }

                _settings.SetPending(pending with { Display = display, Audio = audio, Gameplay = gameplay, Ui = ui });
            }
        }

        if (backPressed)
        {
            _settings.CancelEdit();
            _mode = WorldMode.Menu;
        }
    }

    private void AdjustSettingsValue(int dir)
    {
        var pending = _settings?.Pending;
        if (pending == null) return;

        var display = pending.Display;
        var audio = pending.Audio;
        var gameplay = pending.Gameplay;
        var ui = pending.Ui;

        switch (_settingsItem)
        {
            case SettingsItem.WindowMode:
            {
                int count = Enum.GetValues<WindowMode>().Length;
                int next = (((int)display.WindowMode + dir) % count + count) % count;
                display = display with { WindowMode = (WindowMode)next };
                break;
            }
            case SettingsItem.Resolution:
            {
                if (_resolutions.Count > 0)
                {
                    _resolutionIndex = (((_resolutionIndex + dir) % _resolutions.Count) + _resolutions.Count) % _resolutions.Count;
                    display = display with { Width = _resolutions[_resolutionIndex].Width, Height = _resolutions[_resolutionIndex].Height };
                }
                break;
            }
            case SettingsItem.MasterVolume:
                audio = audio with { MasterVolume = MathHelper.Clamp(audio.MasterVolume + dir * 0.05f, 0f, 1f) };
                break;
            case SettingsItem.BgmVolume:
                audio = audio with { BgmVolume = MathHelper.Clamp(audio.BgmVolume + dir * 0.05f, 0f, 1f) };
                break;
            case SettingsItem.SfxVolume:
                audio = audio with { SfxVolume = MathHelper.Clamp(audio.SfxVolume + dir * 0.05f, 0f, 1f) };
                break;

            case SettingsItem.HudScale:
                ui = ui with { HudScale = Math.Clamp(ui.HudScale + dir * 0.05f, 0.5f, 2.5f) };
                break;

            case SettingsItem.ContinueMode:
            {
                int count = Enum.GetValues<ContinueMode>().Length;
                int next = (((int)gameplay.ContinueMode + dir) % count + count) % count;
                gameplay = gameplay with { ContinueMode = (ContinueMode)next };
                break;
            }
            case SettingsItem.AutoContinueSeconds:
                gameplay = gameplay with { AutoContinueSeconds = Math.Max(0f, gameplay.AutoContinueSeconds + dir * 0.5f) };
                break;

            case SettingsItem.LevelSeed:
                gameplay = gameplay with { LevelSeed = gameplay.LevelSeed + dir };
                break;
        }

        _settings?.SetPending(pending with { Display = display, Audio = audio, Gameplay = gameplay, Ui = ui });
    }

    private void UpdateLevelInterstitial(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        bool confirmPressed = false;
        bool backPressed = false;

        if (inputs != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
                backPressed |= inputs[i].MenuBackPressed;
            }
        }

        var continueMode = _settings?.Current.Gameplay.ContinueMode ?? ContinueMode.PromptThenAuto;

        if (continueMode == ContinueMode.PromptThenAuto && _interstitialTimeLeft > 0f)
            _interstitialTimeLeft -= dt;

        if (confirmPressed || (continueMode == ContinueMode.PromptThenAuto && _interstitialTimeLeft <= 0f))
        {
            var playfield = GetPlayfieldViewport(vp);
            LoadLevel(playfield, _levelIndex);
            ResetBallOnPaddle();
            _mode = WorldMode.Playing;
            return;
        }

        if (backPressed)
        {
            _mode = WorldMode.Menu;
        }
    }

    private void OnLevelCleared(Viewport vp)
    {
        _levelIndex++;

        var continueMode = _settings?.Current.Gameplay.ContinueMode ?? ContinueMode.PromptThenAuto;
        var autoSecs = _settings?.Current.Gameplay.AutoContinueSeconds ?? 2.5f;

        _levelInterstitialWasWin = true;
        _levelInterstitialLine = LevelWinLines[_rng.Next(LevelWinLines.Length)];

        if (continueMode == ContinueMode.Auto)
        {
            var playfield = GetPlayfieldViewport(vp);
            LoadLevel(playfield, _levelIndex);
            ResetBallOnPaddle();
            return;
        }

        _interstitialTimeLeft = continueMode == ContinueMode.PromptThenAuto ? autoSecs : 0f;
        _mode = WorldMode.LevelInterstitial;
    }

    private void OnBallLost(int ballIndex, Viewport vp)
    {
        if ((uint)ballIndex >= (uint)_balls.Count)
            return;

        var lost = _balls[ballIndex];

        if (lost.IsExtraBall)
        {
            _balls.RemoveAt(ballIndex);
            _ballServing.RemoveAt(ballIndex);
            return;
        }

        if (IsCasualNoLose)
        {
            _levelInterstitialWasWin = false;
            _levelInterstitialLine = LevelFailLines[_rng.Next(LevelFailLines.Length)];
            ResetBallOnPaddle(ballIndex);
            return;
        }

        int owner = Math.Clamp(lost.OwnerPlayerIndex, 0, _activePlayerCount - 1);
        bool isPrimaryForOwner = owner < _primaryBallIndexByPlayer.Count && _primaryBallIndexByPlayer[owner] == ballIndex;

        if (isPrimaryForOwner)
        {
            if (owner >= 0 && owner < _livesByPlayer.Count)
                _livesByPlayer[owner]--;
        }

        bool anyAlive = false;
        for (int p = 0; p < _livesByPlayer.Count; p++)
        {
            if (_livesByPlayer[p] > 0)
            {
                anyAlive = true;
                break;
            }
        }

        if (!anyAlive)
        {
            _mode = WorldMode.Menu;
            return;
        }

        ResetBallOnPaddle(ballIndex);
    }

    private void UpdateEffects(float dt)
    {
        if (_toastTimeLeft > 0f)
        {
            _toastTimeLeft -= dt;
            if (_toastTimeLeft <= 0f)
            {
                _toastTimeLeft = 0f;
                _toastText = string.Empty;
            }
        }

        if (_paddleWidthTimeLeft > 0f)
        {
            _paddleWidthTimeLeft -= dt;
            if (_paddleWidthTimeLeft <= 0f)
            {
                _paddleWidthTimeLeft = 0f;
                _paddleWidthMultiplier = 1f;
            }
        }

        if (_ballSpeedTimeLeft > 0f)
        {
            _ballSpeedTimeLeft -= dt;
            if (_ballSpeedTimeLeft <= 0f)
            {
                _ballSpeedTimeLeft = 0f;
                _ballSpeedMultiplier = 1f;

                foreach (var ball in _balls)
                {
                    if (ball.Velocity != Vector2.Zero)
                        ball.Velocity = Vector2.Normalize(ball.Velocity) * GetTargetBallSpeedForLevel();
                }
            }
        }

        if (_scoreMultiplierTimeLeft > 0f)
        {
            _scoreMultiplierTimeLeft -= dt;
            if (_scoreMultiplierTimeLeft <= 0f)
            {
                _scoreMultiplierTimeLeft = 0f;
                _scoreMultiplier = 1f;
            }
        }
    }

    private void ApplyPaddleSize(Viewport vp)
    {
        float targetWidth = _basePaddleSize.X * _difficultyPaddleWidthMultiplier * _paddleWidthMultiplier;
        targetWidth = MathHelper.Clamp(targetWidth, 80f, vp.Width - 30f);

        for (int i = 0; i < _paddles.Count; i++)
        {
            var p = _paddles[i];

            float centerX = p.Center.X;
            float x = centerX - targetWidth * 0.5f;
            x = MathHelper.Clamp(x, 0f, vp.Width - targetWidth);

            _paddles[i] = new Paddle(new Vector2(x, p.Position.Y), new Vector2(targetWidth, _basePaddleSize.Y), _preset.PaddleSpeed);
        }
    }

    private void HandleWallCollisions(Viewport vp, Ball ball)
    {
        if (ball.Position.X - ball.Radius <= 0f)
        {
            ball.Position.X = ball.Radius;
            ball.Velocity.X *= -1f;
        }
        else if (ball.Position.X + ball.Radius >= vp.Width)
        {
            ball.Position.X = vp.Width - ball.Radius;
            ball.Velocity.X *= -1f;
        }

        if (ball.Position.Y - ball.Radius <= 0f)
        {
            ball.Position.Y = ball.Radius;
            ball.Velocity.Y *= -1f;
        }
    }

    private void HandlePaddleCollision(int ballIndex, DragonBreakInput[] inputs, Viewport playfield)
    {
        if ((uint)ballIndex >= (uint)_balls.Count) return;

        var ball = _balls[ballIndex];
        for (int pi = 0; pi < _paddles.Count; pi++)
        {
            var paddle = _paddles[pi];
            if (!ball.Bounds.Intersects(paddle.Bounds))
                continue;

            // Simple reflect upwards.
            ball.Position.Y = paddle.Bounds.Top - ball.Radius - 0.5f;
            ball.Velocity.Y = -Math.Abs(ball.Velocity.Y);

            // Add some horizontal based on where it hit.
            float rel = (ball.Position.X - paddle.Center.X) / Math.Max(1f, paddle.Size.X * 0.5f);
            ball.Velocity.X += rel * 120f;

            // Catch mechanic: if armed, attach the PRIMARY ball to the paddle.
            bool armed = pi < _catchArmedByPlayer.Count && _catchArmedByPlayer[pi];
            if (armed && pi < _primaryBallIndexByPlayer.Count && _primaryBallIndexByPlayer[pi] == ballIndex)
            {
                _ballServing[ballIndex] = true;
                if (ballIndex < _ballCaught.Count) _ballCaught[ballIndex] = true;
                ball.Velocity = Vector2.Zero;

                EnsureBallListsSized(_balls.Count);
                _ballServeOffsetX[ballIndex] = ball.Position.X - paddle.Center.X;

                _catchArmedByPlayer[pi] = false;
                _catchArmedConsumedByPlayer[pi] = true;
            }

            _balls[ballIndex] = ball;
            break;
        }
    }

    private void HandleBrickCollisions(Ball ball)
    {
        for (int i = 0; i < _bricks.Count; i++)
        {
            var b = _bricks[i];
            if (!b.IsAlive) continue;

            if (!ball.Bounds.Intersects(b.Bounds))
                continue;

            var overlap = GetOverlap(ball.Bounds, b.Bounds);
            if (overlap.X != 0)
                ball.Velocity.X *= -1f;
            if (overlap.Y != 0)
                ball.Velocity.Y *= -1f;

            b.HitPoints--;
            if (b.HitPoints <= 0)
            {
                TrySpawnPowerUp(b.Bounds);
            }
            _bricks[i] = b;

            // Score
            int points = (int)(10 * _scoreMultiplier);
            for (int p = 0; p < _scoreByPlayer.Count; p++)
                _scoreByPlayer[p] += points;

            break;
        }
    }

    private static Vector2 GetOverlap(Rectangle a, Rectangle b)
    {
        if (!a.Intersects(b)) return Vector2.Zero;

        int left = b.Left - a.Right;
        int right = b.Right - a.Left;
        int top = b.Top - a.Bottom;
        int bottom = b.Bottom - a.Top;

        int overlapX = Math.Abs(left) < Math.Abs(right) ? left : right;
        int overlapY = Math.Abs(top) < Math.Abs(bottom) ? top : bottom;

        if (Math.Abs(overlapX) < Math.Abs(overlapY))
            return new Vector2(overlapX, 0);

        return new Vector2(0, overlapY);
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        t = MathHelper.Clamp(t, 0f, 1f);
        byte r = (byte)MathHelper.Lerp(a.R, b.R, t);
        byte g = (byte)MathHelper.Lerp(a.G, b.G, t);
        byte bl = (byte)MathHelper.Lerp(a.B, b.B, t);
        return new Color(r, g, bl);
    }

    private Color[] PaletteForHpOwned(int hp, int ownerPlayerIndex)
    {
        var baseColor = PlayerBaseColors[Math.Clamp(ownerPlayerIndex, 0, PlayerBaseColors.Length - 1)];

        var dark = LerpColor(Color.Black, baseColor, 0.55f);
        var mid = LerpColor(Color.Black, baseColor, 0.75f);
        var bright = LerpColor(Color.White, baseColor, 0.85f);

        var palette = new Color[Math.Max(1, hp)];
        for (int i = 0; i < palette.Length; i++)
        {
            float tt = palette.Length <= 1 ? 1f : (float)i / (palette.Length - 1);
            palette[i] = LerpColor(dark, bright, tt);
        }

        if (palette.Length == 2)
            palette[0] = mid;

        return palette;
    }

    private static Color[] PaletteForHp(int hp)
    {
        // Palette from "tough" -> "weak".
        return hp switch
        {
            1 => [new Color(80, 210, 120)],
            2 => [new Color(80, 120, 240), new Color(80, 210, 120)],
            3 => [new Color(150, 80, 230), new Color(80, 120, 240), new Color(80, 210, 120)],
            4 => [new Color(230, 80, 160), new Color(150, 80, 230), new Color(80, 120, 240), new Color(80, 210, 120)],
            5 => [new Color(240, 90, 80), new Color(230, 80, 160), new Color(150, 80, 230), new Color(80, 120, 240), new Color(80, 210, 120)],
            6 => [new Color(240, 140, 70), new Color(240, 90, 80), new Color(230, 80, 160), new Color(150, 80, 230), new Color(80, 120, 240), new Color(80, 210, 120)],
            7 => [new Color(250, 200, 80), new Color(240, 140, 70), new Color(240, 90, 80), new Color(230, 80, 160), new Color(150, 80, 230), new Color(80, 120, 240), new Color(80, 210, 120)],
            _ => [new Color(250, 240, 120), new Color(250, 200, 80), new Color(240, 140, 70), new Color(240, 90, 80), new Color(230, 80, 160), new Color(150, 80, 230), new Color(80, 120, 240), new Color(80, 210, 120)],
        };
    }

    private string SafeText(string text)
    {
        if (string.IsNullOrEmpty(text) || _hudFont == null)
            return text;

        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c == '\n' || c == '\r' || c == '\t') continue;
            if (c >= 32 && c <= 126) continue;
            chars[i] = '?';
        }

        return new string(chars);
    }

    private void LoadLevel(Viewport vp, int levelIndex)
    {
        _bricks.Clear();

        // --- Layout bounds (mobile-safe) ---
        int padding = 6;

        // Keep bricks from spawning under the HUD (HUD scale may change in settings).
        int topMarginBase = 70;
        float hudScale = (_settings?.Current.Ui ?? UiSettings.Default).HudScale;
        float hudSafeTop = _hudFont != null
            ? 8f + (_hudFont.LineSpacing * hudScale) * 3.25f
            : 90f;
        int topMargin = (int)MathF.Ceiling(MathF.Max(topMarginBase, hudSafeTop));

        int sideMargin = 16;
        int bottomMargin = 22;

        int availW = Math.Max(1, vp.Width - sideMargin * 2);
        int availH = Math.Max(1, vp.Height - topMargin - bottomMargin);

        // Choose a brick size that fits smaller/mobile screens.
        // We prefer smaller bricks rather than fewer rows early.
        int desiredCols = 10;
        int minBrickW = 26;
        int maxBrickW = 86;

        int cols = Math.Clamp(desiredCols, 6, 14);
        int brickW = (availW - padding * (cols - 1)) / cols;

        // If too small, reduce column count (bigger bricks) until minimum width is met.
        while (cols > 6 && brickW < minBrickW)
        {
            cols--;
            brickW = (availW - padding * (cols - 1)) / cols;
        }

        // If too large (giant desktop window), increase columns a bit for nicer density.
        while (cols < 14 && brickW > maxBrickW)
        {
            cols++;
            brickW = (availW - padding * (cols - 1)) / cols;
        }

        brickW = Math.Clamp(brickW, minBrickW, maxBrickW);

        int brickH = Math.Clamp((int)MathF.Round(brickW * 0.48f), 16, 30);

        int maxRowsByHeight = Math.Max(1, (availH + padding) / (brickH + padding));
        int rows = Math.Clamp(4 + levelIndex / 2, 4, Math.Min(13, maxRowsByHeight));

        // Recompute capacity based on rows/cols and some safety.
        int capacity = Math.Max(1, rows * cols);

        // --- Deterministic RNG: seed + level + difficulty ---
        int baseSeed = (_settings?.Current.Gameplay ?? GameplaySettings.Default).LevelSeed;
        int levelSeed = HashSeed(baseSeed, levelIndex, (int)_selectedDifficultyId);
        var rng = new Random(levelSeed);

        // --- Difficulty-bound scaling ---
        // Harder difficulties ramp density slightly faster.
        int difficultyIndex = Math.Clamp(DifficultyToPresetIndex(_selectedDifficultyId), 0, Presets.Length - 1);
        float difficultyT = difficultyIndex / (float)Math.Max(1, Presets.Length - 1);

        // Target bricks: odd ramp (3,5,7,...) but limited by capacity.
        int baseTarget = 3 + levelIndex * 2;
        int bonus = (int)MathF.Round(difficultyT * Math.Min(10, levelIndex));
        int target = baseTarget + bonus;
        if ((target & 1) == 0) target++; // keep odd

        // Avoid filling the whole board: keep at most ~80% density.
        int maxTarget = (int)MathF.Floor(capacity * 0.80f);
        target = Math.Clamp(target, 1, Math.Max(1, maxTarget));

        // HP ramps slower; use preset cap.
        int baseHp = 1 + levelIndex / 3;
        baseHp = Math.Min(baseHp, _preset.MaxBrickHp);

        // --- Generate an occupancy grid (complex shapes) ---
        // We'll build a boolean grid of [rows, cols], then instantiate bricks.
        bool[,] occ;
        if (IsOwnedBricksMode && _activePlayerCount == 2)
        {
            // Generate a symmetric layout: build left half, mirror into right half.
            int leftCols = Math.Max(1, cols / 2);
            var half = new bool[rows, leftCols];
            FillOrganic(half, rows, leftCols, target / 2, rng);

            occ = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < leftCols; c++)
                {
                    bool v = half[r, c];
                    occ[r, c] = v;
                    int mirror = cols - 1 - c;
                    if (mirror >= 0 && mirror < cols)
                        occ[r, mirror] = v;
                }
            }
        }
        else
        {
            occ = new bool[rows, cols];
            FillOrganic(occ, rows, cols, target, rng);
        }

        // Seed 1337 special: start with a mythic silhouette, then fill remaining density.
        if (baseSeed == MythicSeed)
        {
            Array.Clear(occ);
            int stamped = ApplyMythicSeed1337Layout(occ, target);
            int remaining = Math.Max(0, target - stamped);
            if (remaining > 0)
                FillOrganic(occ, rows, cols, remaining, rng);
        }

        // Instantiate bricks; apply per-row HP bias but stay inside difficulty bounds.
        int startX = sideMargin;
        int startY = topMargin;

        for (int r = 0; r < rows; r++)
        {
            int rowBonus = r / 3;
            int brickHp = Math.Min(baseHp + rowBonus, _preset.MaxBrickHp);

            for (int c = 0; c < cols; c++)
            {
                if (!occ[r, c])
                    continue;

                int x = startX + c * (brickW + padding);
                int y = startY + r * (brickH + padding);

                // Safety: stay within bounds.
                if (x < sideMargin || x + brickW > vp.Width - sideMargin) continue;
                if (y < topMargin || y + brickH > vp.Height - bottomMargin) continue;

                var bounds = new Rectangle(x, y, brickW, brickH);

                if (IsOwnedBricksMode)
                {
                    int owner;
                    if (_activePlayerCount == 2)
                    {
                        owner = c < cols / 2 ? 0 : 1;
                    }
                    else
                    {
                        owner = Math.Clamp((int)((float)c / cols * _activePlayerCount), 0, _activePlayerCount - 1);
                    }

                    _bricks.Add(new Brick(bounds, brickHp, PaletteForHpOwned(brickHp, owner), ownerPlayerIndex: owner));
                }
                else
                {
                    _bricks.Add(new Brick(bounds, brickHp, PaletteForHp(brickHp)));
                }
            }
        }

        // Failsafe: always at least one brick.
        if (_bricks.Count == 0)
        {
            var bounds = new Rectangle(sideMargin, topMargin, brickW, brickH);
            if (IsOwnedBricksMode)
                _bricks.Add(new Brick(bounds, baseHp, PaletteForHpOwned(baseHp, 0), ownerPlayerIndex: 0));
            else
                _bricks.Add(new Brick(bounds, baseHp, PaletteForHp(baseHp)));
        }
    }

    // Special seed: 1337 generates a mythic/dragon-stamped layout.
    private const int MythicSeed = 1337;

    private static int StampPattern(bool[,] occ, int top, int left, string[] pattern)
    {
        int rows = occ.GetLength(0);
        int cols = occ.GetLength(1);
        int placed = 0;

        for (int pr = 0; pr < pattern.Length; pr++)
        {
            int rr = top + pr;
            if ((uint)rr >= (uint)rows) continue;

            string line = pattern[pr];
            for (int pc = 0; pc < line.Length; pc++)
            {
                int cc = left + pc;
                if ((uint)cc >= (uint)cols) continue;

                if (line[pc] != '#')
                    continue;

                if (!occ[rr, cc])
                {
                    occ[rr, cc] = true;
                    placed++;
                }
            }
        }

        return placed;
    }

    private static int StampPatternCentered(bool[,] occ, int top, string[] pattern)
    {
        int cols = occ.GetLength(1);
        int width = 0;
        for (int i = 0; i < pattern.Length; i++)
            width = Math.Max(width, pattern[i].Length);

        int left = Math.Max(0, (cols - width) / 2);
        return StampPattern(occ, top, left, pattern);
    }

    private static int ApplyMythicSeed1337Layout(bool[,] occ, int target)
    {
        int rows = occ.GetLength(0);
        int cols = occ.GetLength(1);

        // Too small: don't force it.
        if (rows < 6 || cols < 8 || target < 10)
            return 0;

        // Pixel-art patterns. '#' = brick, '.' (or anything else) = empty.
        // These are compact so they still read at small grid sizes.
        string[] dragonHead =
        [
            "....##....",
            "...####...",
            "..######..",
            ".###..###.",
            "###....###",
            "##......##",
        ];

        string[] wyrm =
        [
            "....##........##....",
            "...####......####...",
            "..######....######..",
            ".###..###..###..###.",
            "###....######....###",
            "##......####......##",
            "..##......##......##.",
            "...##............##..",
        ];

        string[] phoenix =
        [
            "##..............##",
            ".##....####....##.",
            "..##..######..##..",
            "...##########...",
            "....########....",
            ".....######.....",
            "......####......",
        ];

        int placed = 0;
        int top = Math.Min(1, Math.Max(0, rows - 1));

        bool canWyrm = rows >= 9 && cols >= 20 && target >= 26;
        bool canPhoenix = rows >= 8 && cols >= 18 && target >= 22;

        if (canWyrm)
            placed += StampPatternCentered(occ, top, wyrm);
        else if (canPhoenix)
            placed += StampPatternCentered(occ, top, phoenix);
        else
            placed += StampPatternCentered(occ, top, dragonHead);

        return Math.Min(placed, Math.Max(0, target));
    }

    private static int HashSeed(int baseSeed, int levelIndex, int difficultyId)
    {
        unchecked
        {
            // Simple stable mix (FNV-ish).
            int h = (int)2166136261;
            h = (h ^ baseSeed) * 16777619;
            h = (h ^ levelIndex) * 16777619;
            h = (h ^ difficultyId) * 16777619;
            // Avoid 0 seed edge.
            return h == 0 ? 1 : h;
        }
    }

    private static void FillOrganic(bool[,] occ, int rows, int cols, int target, Random rng)
    {
        if (target <= 0) return;

        int placed = 0;
        int maxAttempts = Math.Max(200, target * 40);

        // Weight towards arcs (complex) with a secondary cluster pass.
        int arcCount = Math.Clamp(1 + target / 10, 1, 4);
        int clusterCount = Math.Clamp(1 + target / 14, 1, 3);

        // --- Arc/snaking paths ---
        for (int a = 0; a < arcCount && placed < target; a++)
        {
            // Start near top, wander downward.
            float x = rng.Next(0, cols);
            float y = rng.Next(0, Math.Max(1, rows / 2));
            float dx = (float)(rng.NextDouble() * 2 - 1) * 0.9f;
            float dy = 0.6f + (float)rng.NextDouble() * 0.8f;

            int steps = Math.Clamp(target / arcCount + rng.Next(-2, 5), 4, rows * cols);
            for (int i = 0; i < steps && placed < target; i++)
            {
                int r = Math.Clamp((int)MathF.Round(y), 0, rows - 1);
                int c = Math.Clamp((int)MathF.Round(x), 0, cols - 1);

                placed += TryPlace(occ, rows, cols, r, c);

                // Occasionally widen path.
                if (rng.NextDouble() < 0.20 && placed < target)
                {
                    int rr = Math.Clamp(r + rng.Next(-1, 2), 0, rows - 1);
                    int cc = Math.Clamp(c + rng.Next(-1, 2), 0, cols - 1);
                    placed += TryPlace(occ, rows, cols, rr, cc);
                }

                // Move and curve.
                x += dx + (float)(rng.NextDouble() * 2 - 1) * 0.25f;
                y += dy;

                // Bounce off side edges.
                if (x < 0) { x = 0; dx = Math.Abs(dx); }
                if (x > cols - 1) { x = cols - 1; dx = -Math.Abs(dx); }

                // Slightly change direction.
                dx = MathHelper.Clamp(dx + (float)(rng.NextDouble() * 2 - 1) * 0.18f, -1.2f, 1.2f);

                if (y > rows - 1) break;
            }
        }

        // --- Cluster/blob pass ---
        for (int k = 0; k < clusterCount && placed < target; k++)
        {
            int centerR = rng.Next(0, rows);
            int centerC = rng.Next(0, cols);
            int radius = Math.Clamp(1 + target / 18 + rng.Next(0, 2), 1, Math.Max(2, Math.Min(rows, cols) / 2));

            for (int rr = centerR - radius; rr <= centerR + radius && placed < target; rr++)
            {
                if ((uint)rr >= (uint)rows) continue;

                for (int cc = centerC - radius; cc <= centerC + radius && placed < target; cc++)
                {
                    if ((uint)cc >= (uint)cols) continue;

                    float dr = rr - centerR;
                    float dc = cc - centerC;
                    float dist = MathF.Sqrt(dr * dr + dc * dc);
                    float t = dist / Math.Max(1f, radius);

                    // Higher chance closer to center.
                    float p = MathHelper.Clamp(0.95f - t * 0.85f, 0.10f, 0.95f);
                    if (rng.NextDouble() < p)
                        placed += TryPlace(occ, rows, cols, rr, cc);
                }
            }
        }

        // --- Fill remaining with scattered points (keeps early levels quick) ---
        for (int attempt = 0; attempt < maxAttempts && placed < target; attempt++)
        {
            int r = rng.Next(0, rows);
            int c = rng.Next(0, cols);

            // Mild bias towards upper rows so bricks are reachable quickly.
            if (rng.NextDouble() < 0.55)
                r = rng.Next(0, Math.Max(1, rows - 1));

            placed += TryPlace(occ, rows, cols, r, c);
        }
    }

    private static int TryPlace(bool[,] occ, int rows, int cols, int r, int c)
    {
        if ((uint)r >= (uint)rows || (uint)c >= (uint)cols)
            return 0;

        if (occ[r, c])
            return 0;

        // Simple adjacency rule to avoid a super-sparse "dust" look.
        // Allow isolated bricks sometimes (early levels).
        int neighbors = 0;
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int rr = r + dr;
                int cc = c + dc;
                if ((uint)rr >= (uint)rows || (uint)cc >= (uint)cols) continue;
                if (occ[rr, cc]) neighbors++;
            }
        }

        // Place regardless; density is controlled by target. This just assists look.
        occ[r, c] = true;
        return 1;
    }
}
