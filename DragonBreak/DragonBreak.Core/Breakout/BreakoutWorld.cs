#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Audio;
using DragonBreak.Core.Breakout.Entities;
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

    private readonly List<Brick> _bricks = new();
    private readonly List<PowerUp> _powerUps = new();

    private enum WorldMode
    {
        Menu,
        Settings,
        Playing,
        LevelInterstitial,
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
        for (int i = 0; i < _activePlayerCount; i++)
        {
            _catchArmedByPlayer.Add(false);
            _catchArmedConsumedByPlayer.Add(false);
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

    private void Serve(int ballIndex)
    {
        if ((uint)ballIndex >= (uint)_balls.Count) return;

        _ballServing[ballIndex] = false;

        float angle = MathHelper.ToRadians(-90f + _rng.Next(-20, 21));
        float speed = GetTargetBallSpeedForLevel();
        var v = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;

        if (Math.Abs(v.X) < 80f)
            v.X = Math.Sign(v.X == 0 ? 1 : v.X) * 80f;

        _balls[ballIndex].Velocity = v;
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

        // Gameplay uses the playfield viewport (below the HUD bar).
        var playfield = GetPlayfieldViewport(vp);

        UpdateEffects(dt);

        // Update catch armed state (press-to-arm).
        if (inputs != null)
        {
            for (int p = 0; p < _activePlayerCount && p < inputs.Length; p++)
            {
                if (inputs[p].CatchPressed)
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

                _balls[i].Position = _paddles[paddleIndex].Center - new Vector2(0, _balls[i].Radius + 14);

                bool catchReleased = false;
                bool catchPressed = false;
                if (inputs != null && (uint)owner < (uint)inputs.Length)
                {
                    catchReleased = inputs[owner].CatchReleased;
                    catchPressed = inputs[owner].CatchPressed;
                }

                // Space control contract:
                // - Press arms catching (handled earlier)
                // - Release launches a serving/attached PRIMARY ball (initial serve or caught)
                // - Holding alone never launches
                bool isPrimary = owner < _primaryBallIndexByPlayer.Count && _primaryBallIndexByPlayer[owner] == i;

                if (!_balls[i].IsExtraBall && isPrimary)
                {
                    // If the player is holding Space BEFORE the ball was attached, we still want them
                    // to be able to launch once they release, but only after an intentional press.
                    // So: require that catch was pressed at least once (armed) OR that this ball was caught.
                    bool wasCaught = i < _ballCaught.Count && _ballCaught[i];
                    bool hasIntent = (owner < _catchArmedByPlayer.Count && _catchArmedByPlayer[owner]) || wasCaught || catchPressed;

                    if (catchReleased && hasIntent)
                    {
                        Serve(i);

                        if (i < _ballCaught.Count)
                            _ballCaught[i] = false;

                        if (owner < _catchArmedByPlayer.Count)
                        {
                            // Clear armed state so they must press again to enable another catch/launch.
                            _catchArmedByPlayer[owner] = false;
                            _catchArmedConsumedByPlayer[owner] = false;
                        }
                    }
                }

                continue;
            }

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

    private void TrySpawnPowerUp(Rectangle brickBounds)
    {
        // Read chance from settings (configurable), falling back to defaults.
        var gameplay = _settings?.Current.Gameplay ?? GameplaySettings.Default;
        float chance = gameplay.GetPowerUpDropChance(_selectedDifficultyId);
        chance = MathHelper.Clamp(chance, 0f, 1f);

        if (_rng.NextDouble() > chance)
            return;

        double roll = _rng.NextDouble();
        PowerUpType type = roll switch
        {
            < 0.24 => PowerUpType.ExpandPaddle,
            < 0.43 => PowerUpType.SlowBall,
            < 0.58 => PowerUpType.ScoreBoost,
            < 0.73 => PowerUpType.FastBall,
            < 0.88 => PowerUpType.MultiBall,
            _ => PowerUpType.ExtraLife,
        };

        var pos = new Vector2(brickBounds.Center.X, brickBounds.Center.Y);
        _powerUps.Add(new PowerUp(type, pos));
    }

    private string DifficultyLabel(int presetIndex)
    {
        presetIndex = Math.Clamp(presetIndex, 0, Presets.Length - 1);
        // Title-case-ish for display.
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

    private void OnLevelCleared(Viewport vp)
    {
        _levelIndex++;

        // Decide interstitial behavior from settings.
        var continueMode = _settings?.Current.Gameplay.ContinueMode ?? ContinueMode.PromptThenAuto;
        var autoSecs = _settings?.Current.Gameplay.AutoContinueSeconds ?? 2.5f;

        _levelInterstitialWasWin = true;
        _levelInterstitialLine = LevelWinLines[_rng.Next(LevelWinLines.Length)];

        if (continueMode == ContinueMode.Auto)
        {
            LoadLevel(vp, _levelIndex);
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

        // Extra balls: remove without penalty.
        if (lost.IsExtraBall)
        {
            _balls.RemoveAt(ballIndex);
            _ballServing.RemoveAt(ballIndex);
            FixPrimaryBallIndicesAfterRemoval(ballIndex);
            return;
        }

        // Primary balls: in casual we never lose, just reset.
        if (IsCasualNoLose)
        {
            _levelInterstitialWasWin = false;
            _levelInterstitialLine = LevelFailLines[_rng.Next(LevelFailLines.Length)];
            ResetBallOnPaddle(ballIndex);
            return;
        }

        // Non-casual: decrement lives only if this is the owner's primary ball.
        int owner = Math.Clamp(lost.OwnerPlayerIndex, 0, _activePlayerCount - 1);
        bool isPrimaryForOwner = owner < _primaryBallIndexByPlayer.Count && _primaryBallIndexByPlayer[owner] == ballIndex;

        if (isPrimaryForOwner)
        {
            if (owner >= 0 && owner < _livesByPlayer.Count)
                _livesByPlayer[owner]--;
        }

        // If any player's lives drop to 0, that player is out; for now return to menu when all are out.
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

    private void FixPrimaryBallIndicesAfterRemoval(int removedBallIndex)
    {
        for (int p = 0; p < _primaryBallIndexByPlayer.Count; p++)
        {
            int idx = _primaryBallIndexByPlayer[p];
            if (idx == removedBallIndex)
            {
                // Primary ball should never be removed (we only remove extra balls),
                // but recover safely anyway: clamp to 0.
                _primaryBallIndexByPlayer[p] = Math.Clamp(idx, 0, Math.Max(0, _balls.Count - 1));
            }
            else if (idx > removedBallIndex)
            {
                _primaryBallIndexByPlayer[p] = idx - 1;
            }
        }
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
            LoadLevel(vp, _levelIndex);
            ResetBallOnPaddle();
            _mode = WorldMode.Playing;
            return;
        }

        if (backPressed)
        {
            _mode = WorldMode.Menu;
        }
    }

    private void UpdateMenu(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        bool confirmPressed = false, backPressed = false;

        float menuX = 0f;
        float menuY = 0f;

        if (inputs != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
                backPressed |= inputs[i].MenuBackPressed;

                // Use the largest-magnitude axis among players (so any controller can drive the menu).
                if (Math.Abs(inputs[i].MenuMoveX) > Math.Abs(menuX)) menuX = inputs[i].MenuMoveX;
                if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(menuY)) menuY = inputs[i].MenuMoveY;
            }
        }

        bool upHeld = menuY >= MenuAxisDeadzone;
        bool downHeld = menuY <= -MenuAxisDeadzone;
        bool leftHeld = menuX <= -MenuAxisDeadzone;
        bool rightHeld = menuX >= MenuAxisDeadzone;

        // One item per stick "flick" or dpad/keyboard press.
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

                // Persist selected difficulty to settings so casual/no-lose rules apply.
                _selectedPresetIndex = Math.Clamp(_selectedPresetIndex, 0, Presets.Length - 1);
                _selectedDifficultyId = PresetIndexToDifficulty(_selectedPresetIndex);

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
                // Quick UX: confirming a non-start item jumps to Start.
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

        // Open settings with Backspace (menuBack) while on menu.
        // Uses an extra check so you can still reset with Back in the menu.
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

    private void OpenSettings()
    {
        if (_settings == null)
            return;

        _settings.BeginEdit();

        // Sync resolution index from pending settings.
        var pending = _settings.Pending ?? _settings.Current;
        EnsureResolutionIndex(pending.Display.Width, pending.Display.Height);

        _settingsItem = SettingsItem.WindowMode;
        _mode = WorldMode.Settings;
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
        if (_settings == null) return;

        var s = _settings.Current;
        _selectedDifficultyId = s.Gameplay.Difficulty;
        _selectedPresetIndex = Math.Clamp(DifficultyToPresetIndex(_selectedDifficultyId), 0, Presets.Length - 1);
        _preset = Presets[_selectedPresetIndex];
    }

    private void EnsureResolutionIndex(int w, int h)
    {
        _resolutionIndex = 0;
        if (_resolutions.Count == 0)
            return;

        for (int i = 0; i < _resolutions.Count; i++)
        {
            if (_resolutions[i].Width == w && _resolutions[i].Height == h)
            {
                _resolutionIndex = i;
                return;
            }
        }

        // Pick the closest by area if exact match isn't found.
        int targetArea = w * h;
        int best = 0;
        int bestDelta = int.MaxValue;
        for (int i = 0; i < _resolutions.Count; i++)
        {
            int area = _resolutions[i].Width * _resolutions[i].Height;
            int delta = Math.Abs(area - targetArea);
            if (delta < bestDelta)
            {
                best = i;
                bestDelta = delta;
            }
        }

        _resolutionIndex = best;
    }

    private void UpdateSettingsMenu(DragonBreakInput[] inputs, Viewport vp, float dt)
    {
        if (_settings == null)
        {
            _mode = WorldMode.Menu;
            return;
        }

        bool confirmPressed = false, backPressed = false;

        float menuX = 0f;
        float menuY = 0f;

        if (inputs != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
                backPressed |= inputs[i].MenuBackPressed;

                if (Math.Abs(inputs[i].MenuMoveX) > Math.Abs(menuX)) menuX = inputs[i].MenuMoveX;
                if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(menuY)) menuY = inputs[i].MenuMoveY;
            }
        }

        bool upHeld = menuY >= MenuAxisDeadzone;
        bool downHeld = menuY <= -MenuAxisDeadzone;
        bool leftHeld = menuX <= -MenuAxisDeadzone;
        bool rightHeld = menuX >= MenuAxisDeadzone;

        if (upHeld && !_menuUpConsumed)
        {
            int count = Enum.GetValues<SettingsItem>().Length;
            _settingsItem = (SettingsItem)(((int)_settingsItem - 1 + count) % count);
            _menuUpConsumed = true;
        }
        if (!upHeld) _menuUpConsumed = false;

        if (downHeld && !_menuDownConsumed)
        {
            int count = Enum.GetValues<SettingsItem>().Length;
            _settingsItem = (SettingsItem)(((int)_settingsItem + 1) % count);
            _menuDownConsumed = true;
        }
        if (!downHeld) _menuDownConsumed = false;

        if ((leftHeld && !_menuLeftConsumed) || (rightHeld && !_menuRightConsumed))
        {
            int dir = rightHeld ? +1 : -1;
            AdjustSettingsValue(dir);
            _menuLeftConsumed = leftHeld;
            _menuRightConsumed = rightHeld;
        }
        if (!leftHeld) _menuLeftConsumed = false;
        if (!rightHeld) _menuRightConsumed = false;

        if (confirmPressed)
        {
            if (_settingsItem == SettingsItem.LevelSeedRandomize)
            {
                // Crypto-random seed for user-driven re-rolls.
                var pending = _settings.Pending;
                if (pending != null)
                {
                    int seed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
                    var gameplay = pending.Gameplay with { LevelSeed = seed };
                    _settings.SetPending(pending with { Gameplay = gameplay });
                }
            }
            else if (_settingsItem == SettingsItem.LevelSeedReset)
            {
                var pending = _settings.Pending;
                if (pending != null)
                {
                    var gameplay = pending.Gameplay with { LevelSeed = GameplaySettings.Default.LevelSeed };
                    _settings.SetPending(pending with { Gameplay = gameplay });
                }
            }
            else if (_settingsItem == SettingsItem.Apply)
            {
                _settings.ApplyPending();
                SyncSelectionsFromSettings();
                _mode = WorldMode.Menu;
            }
            else if (_settingsItem == SettingsItem.Cancel)
            {
                _settings.CancelEdit();
                SyncSelectionsFromSettings();
                _mode = WorldMode.Menu;
            }
        }

        if (backPressed)
        {
            _settings.CancelEdit();
            SyncSelectionsFromSettings();
            _mode = WorldMode.Menu;
        }
    }

    private void AdjustMenuValue(int dir)
    {
        if (dir == 0) return;

        if (_menuItem == MenuItem.Players)
        {
            _selectedPlayers = Math.Clamp(_selectedPlayers + dir, 1, 4);
        }
        else if (_menuItem == MenuItem.Difficulty)
        {
            _selectedPresetIndex = Math.Clamp(_selectedPresetIndex + dir, 0, Presets.Length - 1);
        }
        else if (_menuItem == MenuItem.GameMode)
        {
            int gmCount = Enum.GetValues<GameMode>().Length;
            _selectedGameMode = (GameMode)(((int)_selectedGameMode + dir + gmCount) % gmCount);
        }
    }

    private void AdjustSettingsValue(int dir)
    {
        if (_settings?.Pending == null)
            return;

        if (dir == 0) return;

        var pending = _settings.Pending;
        var display = pending.Display;
        var audio = pending.Audio;
        var gameplay = pending.Gameplay;
        var ui = pending.Ui;

        const float volStep = 0.05f;

        switch (_settingsItem)
        {
            case SettingsItem.WindowMode:
            {
                int count = Enum.GetValues<Settings.WindowMode>().Length;
                int next = (((int)display.WindowMode + dir) % count + count) % count;
                display = display with { WindowMode = (Settings.WindowMode)next };
                break;
            }
            case SettingsItem.Resolution:
            {
                // Only editable when not borderless.
                if (display.WindowMode == Settings.WindowMode.BorderlessFullscreen)
                    break;

                if (_resolutions.Count == 0)
                    break;

                _resolutionIndex = Math.Clamp(_resolutionIndex + dir, 0, _resolutions.Count - 1);
                var r = _resolutions[_resolutionIndex];
                display = display with { Width = r.Width, Height = r.Height };
                break;
            }
            case SettingsItem.VSync:
                if (dir != 0) display = display with { VSync = !display.VSync };
                break;

            case SettingsItem.MasterVolume:
                audio = audio with { MasterVolume = audio.MasterVolume + dir * volStep };
                break;
            case SettingsItem.BgmVolume:
                audio = audio with { BgmVolume = audio.BgmVolume + dir * volStep };
                break;
            case SettingsItem.SfxVolume:
                audio = audio with { SfxVolume = audio.SfxVolume + dir * volStep };
                break;

            case SettingsItem.HudEnabled:
                ui = ui with { ShowHud = !ui.ShowHud };
                break;
            case SettingsItem.HudScale:
            {
                float step = 0.10f;
                ui = ui with { HudScale = ui.HudScale + dir * step };
                break;
            }
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

            case SettingsItem.ContinueMode:
            {
                int count = Enum.GetValues<ContinueMode>().Length;
                int next = (((int)gameplay.ContinueMode + dir) % count + count) % count;
                gameplay = gameplay with { ContinueMode = (ContinueMode)next };
                break;
            }
            case SettingsItem.AutoContinueSeconds:
            {
                float step = 0.5f;
                gameplay = gameplay with { AutoContinueSeconds = gameplay.AutoContinueSeconds + dir * step };
                break;
            }

            case SettingsItem.LevelSeed:
            {
                // Small step for fine tuning; hold right/left to move faster.
                int step = 1;
                gameplay = gameplay with { LevelSeed = gameplay.LevelSeed + dir * step };
                break;
            }
        }

        _settings.SetPending(pending with { Display = display, Audio = audio, Gameplay = gameplay, Ui = ui });
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
                for (int pi = 0; pi < _paddles.Count; pi++)
                {
                    var newBall = new Ball(_paddles[pi].Center - new Vector2(0, 18), radius: 8f, ownerPlayerIndex: pi, isExtraBall: true)
                    {
                        DrawColor = Color.White,
                    };
                    _balls.Add(newBall);
                    _ballServing.Add(true);
                    _ballCaught.Add(false);
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
        }
    }

    private void UpdateEffects(float dt)
    {
        // Toast timer first (so it updates everywhere gameplay updates)
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

                // Re-normalize to a sensible speed after effect ends.
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
        // Apply width multiplier to each paddle without allocating new paddles.
        float targetWidth = _basePaddleSize.X * _paddleWidthMultiplier;
        targetWidth = MathHelper.Clamp(targetWidth, 80f, vp.Width - 30f);

        for (int i = 0; i < _paddles.Count; i++)
        {
            var p = _paddles[i];

            float centerX = p.Center.X;
            float x = centerX - targetWidth * 0.5f;
            x = MathHelper.Clamp(x, 0f, vp.Width - targetWidth);

            // Paddle.Size is readonly; recreate paddle with new width.
            _paddles[i] = new Paddle(new Vector2(x, p.Position.Y), new Vector2(targetWidth, _basePaddleSize.Y), _preset.PaddleSpeed);
        }
    }

    private void HandleWallCollisions(Viewport vp, Ball ball)
    {
        // Left/right
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

        // Top
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

            // Only the owning paddle can catch/bounce this ball.
            int owner = Math.Clamp(ball.OwnerPlayerIndex, 0, _paddles.Count - 1);
            if (pi != owner)
            {
                // Still allow bounce on other paddles (multiplayer overlap), but do not allow catch.
            }

            bool canCatch = false;
            if (pi == owner && !ball.IsExtraBall)
            {
                bool isPrimary = owner < _primaryBallIndexByPlayer.Count && _primaryBallIndexByPlayer[owner] == ballIndex;

                bool held = inputs != null && (uint)owner < (uint)inputs.Length && inputs[owner].CatchHeld;

                // Only catch if the ball is above the paddle (and moving down or stationary).
                bool ballAbovePaddle = ball.Position.Y < paddle.Bounds.Top;
                bool movingDown = ball.Velocity.Y >= 0f;

                bool armed = owner < _catchArmedByPlayer.Count && _catchArmedByPlayer[owner] && !(_catchArmedConsumedByPlayer.Count > owner && _catchArmedConsumedByPlayer[owner]);

                canCatch = isPrimary && held && ballAbovePaddle && movingDown && armed;
            }

            if (canCatch)
            {
                _ballServing[ballIndex] = true;
                if (ballIndex < _ballCaught.Count)
                    _ballCaught[ballIndex] = true;

                ball.Velocity = Vector2.Zero;

                // Attach to paddle.
                ball.Position = paddle.Center - new Vector2(0, ball.Radius + 14);

                if (owner < _catchArmedConsumedByPlayer.Count)
                    _catchArmedConsumedByPlayer[owner] = true;

                // Only collide with one paddle per update.
                break;
            }

            // Normal bounce.
            ball.Position.Y = paddle.Bounds.Top - ball.Radius - 0.5f;

            float speed = ball.Velocity.Length();
            speed = Math.Max(speed, GetTargetBallSpeedForLevel());

            float paddleCenterX = paddle.Center.X;
            float rel = (ball.Position.X - paddleCenterX) / (paddle.Size.X * 0.5f);
            rel = MathHelper.Clamp(rel, -1f, 1f);

            float maxAngle = MathHelper.ToRadians(65f);
            float angle = MathHelper.Lerp(-maxAngle, maxAngle, (rel + 1f) * 0.5f);

            Vector2 dir = new((float)Math.Sin(angle), -(float)Math.Cos(angle));
            ball.Velocity = Vector2.Normalize(dir) * speed;

            ball.Velocity *= 1.01f;

            // Only collide with one paddle per update.
            break;
        }
    }

    private void HandleBrickCollisions(Ball ball)
    {
        Rectangle ballRect = ball.Bounds;

        for (int i = 0; i < _bricks.Count; i++)
        {
            var brick = _bricks[i];
            if (!brick.IsAlive) continue;

            // In owned-bricks multiplayer, a ball may only damage its owner's bricks.
            if (IsOwnedBricksMode && brick.OwnerPlayerIndex != ball.OwnerPlayerIndex)
                continue;

            if (!ballRect.Intersects(brick.Bounds))
                continue;

            var overlap = GetOverlap(ballRect, brick.Bounds);
            if (overlap == Vector2.Zero)
                continue;

            int beforeHp = brick.HitPoints;
            brick.Hit();

            // SFX
            if (brick.HitPoints <= 0 && beforeHp > 0)
                _audio.PlayBrickBreak1(_totalTime);
            else
                _audio.OnBrickHit(_totalTime);

            int points = 10;
            if (brick.HitPoints <= 0 && beforeHp > 0)
                points += 15;

            int owner = Math.Clamp(ball.OwnerPlayerIndex, 0, Math.Max(0, _scoreByPlayer.Count - 1));
            if (owner >= 0 && owner < _scoreByPlayer.Count)
                _scoreByPlayer[owner] += (int)(points * _scoreMultiplier);

            // (No legacy score mirroring; HUD reads from _scoreByPlayer.)

            if (brick.HitPoints <= 0 && beforeHp > 0)
                TrySpawnPowerUp(brick.Bounds);

            if (Math.Abs(overlap.X) < Math.Abs(overlap.Y))
            {
                ball.Position.X += overlap.X;
                ball.Velocity.X *= -1f;
            }
            else
            {
                ball.Position.Y += overlap.Y;
                ball.Velocity.Y *= -1f;
            }

            // Only handle one brick per frame for stability.
            break;
        }
    }

    // --- Helpers restored (were accidentally removed) ---

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

        // Level + mode title in top center.
        string top = $"LEVEL {_levelIndex + 1}   MODE {_selectedGameMode}";
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
    }

    /// <summary>
    /// Draws gameplay only (playfield: bricks/paddles/balls/powerups).
    /// This is intended to be called with a scissor rect that clips to the playfield.
    /// </summary>
    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // Background
        sb.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), new Color(16, 16, 20));

        // If we're in menu/settings/interstitial, we still render the playfield background,
        // but UI is handled via DrawUi() to avoid scissor clipping.

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

            sb.Draw(_pixel, b.Bounds, c);
        }

        // Paddles
        for (int i = 0; i < _paddles.Count; i++)
            sb.Draw(_pixel, _paddles[i].Bounds, PlayerBaseColors[Math.Clamp(i, 0, PlayerBaseColors.Length - 1)]);

        // Balls
        for (int i = 0; i < _balls.Count; i++)
        {
            var ball = _balls[i];
            var r = ball.Bounds;

            var c = ball.DrawColor ?? (ball.IsExtraBall ? Color.White : PlayerBaseColors[Math.Clamp(ball.OwnerPlayerIndex, 0, PlayerBaseColors.Length - 1)]);
            sb.Draw(_pixel, r, c);
        }

        // PowerUps
        for (int i = 0; i < _powerUps.Count; i++)
            sb.Draw(_pixel, _powerUps[i].Bounds, Color.Gold);
    }

    /// <summary>
    /// Draws screen-space UI (menus/settings/HUD/top bar). Call this WITHOUT scissor.
    /// </summary>
    public void DrawUi(SpriteBatch sb, Viewport vp)
    {
        // Menu / settings take over the screen.
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

        // Otherwise, show the in-game HUD (single and multiplayer).
        DrawHud(sb, vp);

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

            // Simple fade out.
            float a = MathHelper.Clamp(_toastTimeLeft / ToastDurationSeconds, 0f, 1f);
            var col = Color.White * a;
            var shadow = Color.Black * (0.65f * a);

            sb.DrawString(_hudFont, _toastText, new Vector2(x + 1, y + 1), shadow, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_hudFont, _toastText, new Vector2(x, y), col, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
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
            _ => type.ToString(),
        };
}
