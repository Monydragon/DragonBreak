#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Audio;
using DragonBreak.Core.Breakout.Entities;
using DragonBreak.Core.Breakout.Ui;
using DragonBreak.Core.Graphics;
using DragonBreak.Core.Input;
using DragonBreak.Core.Settings;
using DragonBreak.Core.Highscores;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DragonBreak.Core.Breakout;

public sealed partial class BreakoutWorld
{
    private readonly Random _rng = new();

    // Touch-drag state for grabbing paddles (mobile / multitouch).
    private readonly Dictionary<int, int> _touchToPaddleIndex = new();
    private readonly Dictionary<int, Vector2> _touchToPaddleOffset = new();

    private void ClearTouchDragState()
    {
        _touchToPaddleIndex.Clear();
        _touchToPaddleOffset.Clear();
    }

    // Track prior raw input for debug-only combos (separate from InputMapper).
    private KeyboardState _prevDebugKeyboard;
    private readonly GamePadState[] _prevDebugPadByPlayer = new GamePadState[4];

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
        HighScores,
        NameEntry,
        GameOver,
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
        HighScores,
        Settings,
        Start,
    }

    private MenuItem _menuItem = MenuItem.Start;

    // Menu debouncing so navigation only moves one step per input.
    private bool _menuUpConsumed;
    private bool _menuDownConsumed;
    private bool _menuLeftConsumed;
    private bool _menuRightConsumed;

    // Highscores + game over UI
    private HighScoreService? _highscores;
    private HighScoresScreen? _highScoresScreen;
    private NameEntryScreen? _nameEntryScreen;
    private GameOverScreen? _gameOverScreen;

    // Highscores screen return target (menu vs paused)
    private WorldMode _returnModeAfterHighScores = WorldMode.Menu;

    private GameModeId CurrentModeId
        => _selectedGameMode switch
        {
            GameMode.Arcade => GameModeId.Arcade,
            GameMode.Story => GameModeId.Story,
            GameMode.Puzzle => GameModeId.Puzzle,
            _ => GameModeId.Arcade,
        };

    private void ShowHighScores(WorldMode returnTo)
    {
        if (_highScoresScreen == null)
            return;

        // Ensure mode/difficulty reflect the current menu selections when opening highscores.
        _selectedPresetIndex = Math.Clamp(_selectedPresetIndex, 0, Presets.Length - 1);
        _selectedDifficultyId = PresetIndexToDifficulty(_selectedPresetIndex);

        _returnModeAfterHighScores = returnTo;
        _highScoresScreen.Show(CurrentModeId, _selectedDifficultyId);
        _mode = WorldMode.HighScores;
    }

    // Settings debouncing so left/right changes only move one step per input.
    private bool _settingsLeftConsumed;
    private bool _settingsRightConsumed;

    // While holding left/right in settings, we repeat adjustments at an interval.
    private float _settingsAdjustRepeatTime;
    private const float SettingsAdjustRepeatInitialDelay = 0.35f;
    private const float SettingsAdjustRepeatInterval = 0.075f;

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

        ContinueMode,
        AutoContinueSeconds,

        LevelSeed,
        LevelSeedRandomize,
        LevelSeedReset,

        DebugMode,

        Apply,
        Cancel,
    }

    private SettingsItem _settingsItem = SettingsItem.WindowMode;

    // Settings UI is currently a simplified list (not all enum items are shown).
    // Keep navigation consistent with what we render by tracking a selection index
    // over the visible items instead of wrapping over every enum value.
    private int _settingsSelectedIndex;

    private static readonly SettingsItem[] SettingsMenuItemsTopToBottom =
    [
        SettingsItem.WindowMode,
        SettingsItem.Resolution,
        SettingsItem.VSync,
        SettingsItem.MasterVolume,
        SettingsItem.BgmVolume,
        SettingsItem.SfxVolume,
        SettingsItem.HudEnabled,
        SettingsItem.HudScale,
        SettingsItem.ContinueMode,
        SettingsItem.AutoContinueSeconds,
        SettingsItem.LevelSeed,
        SettingsItem.LevelSeedRandomize,
        SettingsItem.LevelSeedReset,
        SettingsItem.DebugMode,
        SettingsItem.Apply,
        SettingsItem.Cancel,
    ];

    private void SyncSettingsSelectedIndexFromItem()
    {
        int idx = Array.IndexOf(SettingsMenuItemsTopToBottom, _settingsItem);
        _settingsSelectedIndex = idx >= 0 ? idx : 0;
        _settingsItem = SettingsMenuItemsTopToBottom[Math.Clamp(_settingsSelectedIndex, 0, SettingsMenuItemsTopToBottom.Length - 1)];
    }

    private void SyncSettingsItemFromSelectedIndex()
    {
        if (SettingsMenuItemsTopToBottom.Length == 0)
        {
            _settingsSelectedIndex = 0;
            return;
        }

        _settingsSelectedIndex = Math.Clamp(_settingsSelectedIndex, 0, SettingsMenuItemsTopToBottom.Length - 1);
        _settingsItem = SettingsMenuItemsTopToBottom[_settingsSelectedIndex];
    }

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
        ClearTouchDragState();

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

        _highscores = services.GetService(typeof(HighScoreService)) as HighScoreService;
        if (_highscores != null)
        {
            _highScoresScreen = new HighScoresScreen(_highscores);
            _nameEntryScreen = new NameEntryScreen();
            _gameOverScreen = new GameOverScreen(_highscores);
        }

        if (_graphics != null && _displayModes != null)
        {
            _resolutions = new List<ResolutionOption>(_displayModes.GetSupportedResolutions(_graphics));
        }

        SyncSelectionsFromSettings();
    }

    // Forward raw typed characters from the Game (Window.TextInput) to the active UI screen.
    public void OnTextInput(char c)
    {
        if (_mode == WorldMode.NameEntry && _nameEntryScreen != null)
            _nameEntryScreen.OnTextInput(c);
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

        // Players start active at the beginning of a new game.
        _playerActiveByIndex.Clear();
        for (int i = 0; i < _activePlayerCount; i++)
        {
            _playerActiveByIndex.Add(true);
        }

        // Normal and below: no forced life queued at game start.
        _forceNextPowerUpLife = false;

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
        {
            _primaryBallIndexByPlayer.Add(i);
        }

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
            ClearTouchDragState();
            UpdateMenu(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.Settings)
        {
            ClearTouchDragState();
            UpdateSettingsMenu(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.LevelInterstitial)
        {
            ClearTouchDragState();
            UpdateLevelInterstitial(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.Paused)
        {
            ClearTouchDragState();
            UpdatePaused(inputs, vp, dt);
            return;
        }

        if (_mode == WorldMode.HighScores)
        {
            if (_highScoresScreen == null)
            {
                _mode = WorldMode.Menu;
                return;
            }

            _highScoresScreen.Update(inputs, vp, dt);
            if (_highScoresScreen.ConsumeAction() == HighScoresScreen.HighScoresAction.Back)
            {
                // Return to whichever screen opened highscores.
                _mode = _returnModeAfterHighScores;
            }
            return;
        }

        if (_mode == WorldMode.NameEntry)
        {
            if (_nameEntryScreen == null || _highscores == null)
            {
                _mode = WorldMode.Menu;
                return;
            }

            _nameEntryScreen.Update(inputs, vp, dt);
            switch (_nameEntryScreen.ConsumeAction())
            {
                case NameEntryScreen.NameEntryAction.Submitted:
                {
                    var ctx = _nameEntryScreen.GetContext();
                    var name = _nameEntryScreen.GetSubmittedName();
                    var entry = HighScoreEntry.Create(name, ctx.FinalScore, ctx.Mode, ctx.Difficulty, ctx.LevelReached, ctx.Seed);
                    bool saved = _highscores.TrySubmit(entry);

                    _gameOverScreen?.Show(ctx.FinalScore, ctx.Mode, ctx.Difficulty, ctx.LevelReached, ctx.Seed, showSavedMessage: saved);
                    _mode = WorldMode.GameOver;
                    break;
                }
                case NameEntryScreen.NameEntryAction.Canceled:
                {
                    var ctx = _nameEntryScreen.GetContext();
                    _gameOverScreen?.Show(ctx.FinalScore, ctx.Mode, ctx.Difficulty, ctx.LevelReached, ctx.Seed, showSavedMessage: false);
                    _mode = WorldMode.GameOver;
                    break;
                }
            }
            return;
        }

        if (_mode == WorldMode.GameOver)
        {
            if (_gameOverScreen == null)
            {
                _mode = WorldMode.Menu;
                return;
            }

            _gameOverScreen.Update(inputs, vp, dt);
            switch (_gameOverScreen.ConsumeAction())
            {
                case GameOverScreen.GameOverAction.Retry:
                    StartNewGame(vp, _preset);
                    _mode = WorldMode.Playing;
                    break;
                case GameOverScreen.GameOverAction.MainMenu:
                    _mode = WorldMode.Menu;
                    _menuItem = MenuItem.Start;
                    break;
            }
            return;
        }

        // Gameplay uses the playfield viewport (below the HUD bar).
        var playfield = GetPlayfieldViewport(vp);

        // --- Mobile touch: PAUSE button (top-right in HUD bar) ---
        // Keep it away from score/lives which are typically left-aligned.
        if (inputs != null && inputs.Length > 0 && inputs[0].Touches.TryGetBegan(out var pauseTap) && pauseTap.IsTap)
        {
            float x = pauseTap.X01 * vp.Width;
            float y = pauseTap.Y01 * vp.Height;

            var ui = _settings?.Current.Ui ?? UiSettings.Default;
            float scale = ui.HudScale;
            float pauseScale = scale * 0.75f;
            var pauseText = "PAUSE";
            var size = _hudFont != null ? _hudFont.MeasureString(pauseText) * pauseScale : new Vector2(64, 20);

            // Top-right placement.
            int w = (int)(size.X + 28);
            int h = (int)(size.Y + 16);
            int pad = 12;
            var pauseRect = new Rectangle(vp.Width - w - pad, 10, w, h);

            if (pauseRect.Contains((int)x, (int)y))
            {
                if (!_pauseTapConsumed)
                {
                    _pauseTapConsumed = true;
                    EnterPaused();
                    return;
                }
            }
            else
            {
                _pauseTapConsumed = false;
            }
        }
        else
        {
            _pauseTapConsumed = false;
        }

        // --- Mobile touch: allow 2nd finger as a catch/launch gesture while still dragging with 1st finger ---
        // When any extra finger (besides the main dragging finger) is down, treat it as holding catch.
        // When that extra finger is released, treat it as catch release (launch if ball is serving).
        bool anyTouch = inputs != null && inputs.Length > 0 && inputs[0].Touches.HasAny;
        int touchCount = anyTouch ? inputs![0].Touches.Points.Count : 0;

        bool extraFingerNow = touchCount >= 2;
        if (extraFingerNow)
        {
            _twoThumbsHeld = true;

            if (_activePlayerCount > 0 && _catchArmedByPlayer.Count > 0)
            {
                _catchArmedByPlayer[0] = true;
                if (_catchArmedConsumedByPlayer.Count > 0)
                    _catchArmedConsumedByPlayer[0] = false;
            }
        }
        else if (_twoThumbsHeld)
        {
            _twoThumbsHeld = false;

            if (_activePlayerCount > 0 && _primaryBallIndexByPlayer.Count > 0)
            {
                int bi = _primaryBallIndexByPlayer[0];
                if ((uint)bi < (uint)_ballServing.Count && _ballServing[bi])
                {
                    bool suppressed = _launchSuppressedByPlayer.Count > 0 && _launchSuppressedByPlayer[0];
                    if (!suppressed)
                    {
                        Serve(bi);
                        if (bi < _ballCaught.Count) _ballCaught[bi] = false;
                    }
                }

                if (_catchArmedByPlayer.Count > 0)
                    _catchArmedByPlayer[0] = false;
            }
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
        float minY = GetPaddleMinY(playfield);

        // Safety for small windows: ensure a valid clamp range so paddles can still move.
        if (maxY < minY)
            minY = maxY;

        // Mobile: multi-touch grab + drag (X/Y). Run once per frame and let it drive any captured paddles.
        // Use player 1's touch stream (all mobile touches are injected there currently).
        if (inputs != null && inputs.Length > 0 && inputs[0].Touches != null && inputs[0].Touches.HasAny)
        {
            UpdateTouchDragForPaddles(inputs[0], playfield, dt, minY, maxY);
        }

        for (int i = 0; i < _paddles.Count; i++)
        {
            float moveX = 0f;
            float moveY = 0f;
            DragonBreakInput input = default;

            if (inputs != null && i < inputs.Length)
            {
                input = inputs[i];
                moveX = input.MoveX;
                moveY = input.MoveY;
            }

            // If this paddle is currently being dragged by any touch, skip keyboard/controller move for this frame.
            bool beingDragged = false;
            foreach (var kvp in _touchToPaddleIndex)
            {
                if (kvp.Value == i)
                {
                    beingDragged = true;
                    break;
                }
            }

            if (beingDragged)
                continue;

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

                // Ensure serving balls never end up above the visible playfield.
                // (Gameplay is playfield-local; top edge is y=0.)
                if (_balls[i].Position.Y < _balls[i].Radius)
                    _balls[i].Position = new Vector2(_balls[i].Position.X, _balls[i].Radius);

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

            case PauseMenuScreen.PauseAction.HighScores:
                ShowHighScores(WorldMode.Paused);
                break;

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

        // Apply forced life logic after random selection.
        if (!casual && _forceNextPowerUpLife)
        {
            type = PowerUpType.ExtraLife;
            _forceNextPowerUpLife = false;
        }

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

                // Life up revive policy:
                // - Normal and below: it can revive downed players, and we still also respawn between levels.
                // - Hard/VeryHard: ONLY way to revive is life up.
                // - Extreme: no revives at all.
                if (!IsCasualNoLose && AllowsLifeUpRespawn())
                {
                    for (int p = 0; p < _activePlayerCount && p < _playerActiveByIndex.Count; p++)
                    {
                        if (!_playerActiveByIndex[p] && p < _livesByPlayer.Count && _livesByPlayer[p] > 0)
                            TryActivatePlayer(p);
                    }
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

    private void OnLevelCleared(Viewport vp)
    {
        _levelIndex++;

        // Normal and below: between-level respawn.
        if (!IsCasualNoLose && AllowsLevelRespawn())
        {
            for (int p = 0; p < _activePlayerCount && p < _playerActiveByIndex.Count; p++)
            {
                if (!_playerActiveByIndex[p] && p < _livesByPlayer.Count && _livesByPlayer[p] > 0)
                    TryActivatePlayer(p);
            }
        }

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

        // Continue to next level.
        if (confirmPressed || (continueMode == ContinueMode.PromptThenAuto && _interstitialTimeLeft <= 0f))
        {
            _forceNextPowerUpLife = false;

            var playfield = GetPlayfieldViewport(vp);
            LoadLevel(playfield, _levelIndex);
            ResetBallOnPaddle();
            _mode = WorldMode.Playing;
            return;
        }

        if (backPressed)
        {
            // Back out to menu.
            _mode = WorldMode.Menu;
            _menuItem = MenuItem.Start;
        }
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
            _livesByPlayer[owner]--;

            // If this player's lives hit 0, they are downed and should stop playing.
            if (_livesByPlayer[owner] <= 0)
            {
                EliminatePlayer(owner);

                // Normal and below: queue up a life-up drop to revive a downed player.
                if (AllowsLevelRespawn())
                    _forceNextPowerUpLife = true;
            }
        }

        // Multiplayer game over condition: only when ALL players are down.
        bool anyActive = AnyPlayerActive();

        if (!anyActive)
        {
            int score = _scoreByPlayer.Count > 0 ? _scoreByPlayer[0] : 0;
            int seed = _settings?.Current?.Gameplay?.LevelSeed ?? 0;

            if (_highscores != null && _nameEntryScreen != null)
            {
                // Only prompt for name if this score would make the local leaderboard.
                var probe = HighScoreEntry.Create("PLAYER", score, CurrentModeId, _selectedDifficultyId, _levelIndex, seed);
                if (_highscores.WouldQualify(probe))
                {
                    _nameEntryScreen.Show(playerIndex: 0, score, CurrentModeId, _selectedDifficultyId, _levelIndex, seed);
                    _mode = WorldMode.NameEntry;
                    return;
                }
            }

            if (_gameOverScreen != null)
            {
                _gameOverScreen.Show(score, CurrentModeId, _selectedDifficultyId, _levelIndex, seed, showSavedMessage: false);
                _mode = WorldMode.GameOver;
            }
            else
            {
                _mode = WorldMode.Menu;
            }
            return;
        }

        // If the owner is downed, don't reset their ball on paddle.
        if (_playerActiveByIndex.Count > owner && !_playerActiveByIndex[owner])
            return;

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

    private float GetPaddleMinY(Viewport playfield)
    {
        // Old behavior: allow paddles up to a fixed fraction of the playfield.
        float minY = Math.Max(0f, playfield.Height * PaddleMaxUpScreenFraction);

        // New behavior: never allow paddles to overlap the brick field.
        // Bricks/paddles are simulated in playfield-local coordinates, so this is safe.
        int maxBrickBottom = -1;
        for (int i = 0; i < _bricks.Count; i++)
        {
            if (!_bricks[i].IsAlive) continue;
            maxBrickBottom = Math.Max(maxBrickBottom, _bricks[i].Bounds.Bottom);
        }

        if (maxBrickBottom >= 0)
        {
            // Keep some breathing room below the last (lowest) brick row.
            const int pad = 10;
            minY = Math.Max(minY, maxBrickBottom + pad);
        }

        // Ensure minY doesn't push the paddle out of bounds in tiny windows.
        minY = MathHelper.Clamp(minY, 0f, Math.Max(0f, playfield.Height - _basePaddleSize.Y));
        return minY;
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

    private void HandlePaddleCollision(int ballIndex, DragonBreakInput[]? inputs, Viewport playfield)
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
        // Palette is ordered from "tough" -> "weak".
        // Requested mapping by remaining hits:
        // 1=White, 2=Red, 3=Orange, 4=Purple, 5=Green, 6=Cyan, 7=Pink, 8=Violet.
        // Since the palette is "toughest -> weakest", we build arrays from hp down to 1.
        static Color HpColor(int remainingHp)
        {
            return remainingHp switch
            {
                1 => Color.White,
                2 => Color.Red,
                3 => Color.Orange,
                4 => Color.Purple,
                5 => Color.Green,
                6 => Color.Cyan,
                7 => Color.HotPink,
                // MonoGame/XNA doesn’t have a named "Violet" in the standard Color set.
                // Use a close preset-ish violet tone.
                _ => new Color(143, 0, 255),
            };
        }

        int clamped = Math.Clamp(hp, 1, 8);
        var palette = new Color[clamped];
        for (int i = 0; i < palette.Length; i++)
        {
            int remaining = clamped - i;
            palette[i] = HpColor(remaining);
        }

        return palette;
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

        // Rows: slower early escalation + clearer tier steps.
        // LevelIndex is 0-based, so 0..9 are the first 10 levels.
        int tierIndex = Math.Max(0, levelIndex / 10);
        int rowsBase = 4;
        int rowsEarly = Math.Min(2, levelIndex / 3);         // 0..9 => +0..2
        int rowsTier = Math.Min(6, tierIndex * 2);           // 0,2,4,6... (clamped)
        int rows = Math.Clamp(rowsBase + rowsEarly + rowsTier, 4, Math.Min(13, maxRowsByHeight));

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

        // Target bricks: slower early ramp + tier jumps, but still capped by capacity.
        // Keep odd so layouts tend to look centered.
        int baseTarget = 5 + levelIndex;                      // slower than old (was 3 + levelIndex*2)
        int tierTargetJump = tierIndex * 8;                   // noticeable change every 10 levels
        int bonus = (int)MathF.Round(difficultyT * Math.Min(10, levelIndex));
        int target = baseTarget + tierTargetJump + bonus;
        if ((target & 1) == 0) target++;

        // Avoid filling the whole board: keep at most ~75% density (slightly looser early feel).
        int maxTarget = (int)MathF.Floor(capacity * 0.75f);
        target = Math.Clamp(target, 1, Math.Max(1, maxTarget));

        // --- HP tiers (every 10 levels) ---
        // Option C: same tier unlocks across difficulties, but distribution shifts with difficulty.
        // Levels 0..9 => only 1HP bricks.
        int unlockedHpMax = Math.Clamp(1 + tierIndex, 1, _preset.MaxBrickHp);

        // Weighted random selection so not all bricks sit at the tier cap.
        // Returns an HP in [1..unlockedHpMax].
        int RollBrickHp(int rowIndex)
        {
            if (unlockedHpMax <= 1)
                return 1;

            // A tiny upward bias for higher rows, but much gentler than the old r/3 step.
            // This keeps tops a bit tougher without feeling like a sudden ramp.
            float rowT = rows <= 1 ? 0f : rowIndex / (float)(rows - 1);

            // Difficulty bias: on harder modes, the upper end shows up a bit more often.
            // difficultyT in [0..1]
            float hardBias = 0.10f + 0.20f * difficultyT; // 0.10..0.30

            // Tier bias: in later tiers, allow the cap to appear a little more often.
            float tierBias = 0.08f * tierIndex; // grows slowly

            // Base probabilities: heavily favor low HP; allow some mid; rare cap.
            // We build weights for 1..unlockedHpMax.
            // Weight formula: w(h) = exp(-k*(h-1)) with k varying by difficulty/tier/row.
            float k = 1.35f - (hardBias + tierBias + rowT * 0.20f); // smaller k => more high HP
            k = MathHelper.Clamp(k, 0.55f, 1.35f);

            float total = 0f;
            Span<float> weights = stackalloc float[9]; // supports hp up to 8 comfortably
            int max = Math.Min(unlockedHpMax, 8);
            for (int h = 1; h <= max; h++)
            {
                float w = MathF.Exp(-k * (h - 1));
                weights[h] = w;
                total += w;
            }

            float pick = (float)rng.NextDouble() * total;
            float acc = 0f;
            for (int h = 1; h <= max; h++)
            {
                acc += weights[h];
                if (pick <= acc)
                    return h;
            }

            return max;
        }

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

        // Instantiate bricks; pick per-brick HP with gentle row bias, capped by tier + difficulty.
        int startX = sideMargin;
        int startY = topMargin;

        for (int r = 0; r < rows; r++)
        {
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

                int brickHp = RollBrickHp(r);

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
            int brickHp = 1;
            if (IsOwnedBricksMode)
                _bricks.Add(new Brick(bounds, brickHp, PaletteForHpOwned(brickHp, 0), ownerPlayerIndex: 0));
            else
                _bricks.Add(new Brick(bounds, brickHp, PaletteForHp(brickHp)));
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

    // --- Multiplayer downed/respawn state ---

    // When a player is downed, their paddle is disabled and their primary ball is parked until revived.
    private readonly List<bool> _playerActiveByIndex = new();

    // Normal and below: if any player is downed, force the next power-up drop to be ExtraLife.
    private bool _forceNextPowerUpLife;

    private bool AllowsLevelRespawn()
        => _selectedDifficultyId is DifficultyId.Casual or DifficultyId.VeryEasy or DifficultyId.Easy or DifficultyId.Normal;

    private bool AllowsLifeUpRespawn()
        => _selectedDifficultyId is DifficultyId.Casual or DifficultyId.VeryEasy or DifficultyId.Easy or DifficultyId.Normal
            or DifficultyId.Hard or DifficultyId.VeryHard;

    private bool AnyPlayerDowned()
    {
        for (int p = 0; p < _activePlayerCount && p < _playerActiveByIndex.Count; p++)
        {
            if (!_playerActiveByIndex[p])
                return true;
        }
        return false;
    }

    private bool AnyPlayerActive()
    {
        for (int p = 0; p < _activePlayerCount && p < _playerActiveByIndex.Count; p++)
        {
            if (_playerActiveByIndex[p])
                return true;
        }
        return false;
    }

    private void EliminatePlayer(int playerIndex)
    {
        if ((uint)playerIndex >= (uint)_activePlayerCount)
            return;

        if (playerIndex < _playerActiveByIndex.Count)
            _playerActiveByIndex[playerIndex] = false;

        // Park primary ball (keep index stable).
        if (playerIndex < _primaryBallIndexByPlayer.Count)
        {
            int bi = _primaryBallIndexByPlayer[playerIndex];
            if ((uint)bi < (uint)_balls.Count)
            {
                _ballServing[bi] = false;
                _balls[bi].Velocity = Vector2.Zero;
                _balls[bi].Position = new Vector2(-9999, -9999);
            }
        }

        // Remove any extra balls owned by this player.
        for (int i = _balls.Count - 1; i >= 0; i--)
        {
            if (_balls[i].OwnerPlayerIndex != playerIndex || !_balls[i].IsExtraBall)
                continue;

            _balls.RemoveAt(i);
            _ballServing.RemoveAt(i);
            if (i < _ballCaught.Count) _ballCaught.RemoveAt(i);

            for (int p = 0; p < _primaryBallIndexByPlayer.Count; p++)
            {
                if (_primaryBallIndexByPlayer[p] > i)
                    _primaryBallIndexByPlayer[p]--;
            }
        }
    }

    private void TryActivatePlayer(int playerIndex)
    {
        if ((uint)playerIndex >= (uint)_activePlayerCount)
            return;
        if (playerIndex >= _livesByPlayer.Count || _livesByPlayer[playerIndex] <= 0)
            return;

        if (playerIndex < _playerActiveByIndex.Count)
            _playerActiveByIndex[playerIndex] = true;

        if (playerIndex < _launchSuppressedByPlayer.Count)
            _launchSuppressedByPlayer[playerIndex] = true;

        if (playerIndex < _primaryBallIndexByPlayer.Count)
        {
            int bi = _primaryBallIndexByPlayer[playerIndex];
            if ((uint)bi < (uint)_balls.Count)
                ResetBallOnPaddle(bi);
        }
    }

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

    private void SyncSelectionsFromSettings()
    {
        if (_settings == null)
            return;

        var s = _settings.Current;
        _selectedDifficultyId = s.Gameplay.Difficulty;
        _selectedPresetIndex = Math.Clamp(DifficultyToPresetIndex(_selectedDifficultyId), 0, Presets.Length - 1);
        _preset = Presets[_selectedPresetIndex];
    }

    /// <summary>
    /// Platform hint: main menu and other UI-only screens should be portrait; gameplay can use sensors.
    /// Android (or other platforms) can poll this and adjust RequestedOrientation.
    /// </summary>
    public bool WantsPortraitOrientation => IsMenuLikeMode(_mode);

    private static bool IsMenuLikeMode(WorldMode mode)
        => mode is WorldMode.Menu or WorldMode.Settings or WorldMode.HighScores or WorldMode.NameEntry or WorldMode.GameOver;

    // --- Touch smoothing + gestures ---
    // When dragging paddles via touch, apply a light smoothing so movement feels less jittery.
    private const float TouchDragSmoothingHz = 18f;

    // Two-thumb command: when 2+ touches are active, interpret as holding catch.
    // When that multi-touch ends, interpret as releasing catch (launch).
    private bool _twoThumbsHeld;

    // HUD pause button (top-left of screen).
    private bool _pauseTapConsumed;
}
