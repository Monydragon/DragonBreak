#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Breakout.Entities;
using DragonBreak.Core.Graphics;
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

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
        Playing,
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

    private int _activePlayerCount = 1;

    private Vector2 _basePaddleSize;

    // Powerup effects (simple timed multipliers)
    private float _paddleWidthMultiplier = 1f;
    private float _paddleWidthTimeLeft;

    private float _ballSpeedMultiplier = 1f;
    private float _ballSpeedTimeLeft;

    private float _scoreMultiplier = 1f;
    private float _scoreMultiplierTimeLeft;

    private int _levelIndex;
    private int _lives;
    private int _score;

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
        Start,
    }

    private MenuItem _menuItem = MenuItem.Start;

    // Menu debouncing so navigation only moves one step per input.
    private bool _menuUpConsumed;
    private bool _menuDownConsumed;
    private bool _menuLeftConsumed;
    private bool _menuRightConsumed;

    private const float MenuAxisDeadzone = 0.55f;

    // Menu repeat handling
    private float _menuRepeatCooldown;
    private float _menuHoldTime;

    private const float MenuInitialRepeatDelaySeconds = 0.28f;
    private const float MenuRepeatRateSeconds = 0.11f;

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

        _mode = WorldMode.Menu;
        _selectedPresetIndex = 3; // default: normal
        _preset = Presets[_selectedPresetIndex];

        CreateEntitiesForViewport(graphicsDevice.Viewport);
        StartNewGame(graphicsDevice.Viewport, _preset);

        // Start with menu visible.
        _mode = WorldMode.Menu;
    }

    private void StartNewGame(Viewport vp, DifficultyPreset preset)
    {
        _preset = preset;

        _levelIndex = 0;
        _lives = _preset.StartingLives;
        _score = 0;

        _powerUps.Clear();
        ClearEffects();

        CreateEntitiesForViewport(vp);
        LoadLevel(vp, _levelIndex);
        ResetBallOnPaddle();
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

        _basePaddleSize = new Vector2(Math.Max(120, vp.Width / 7), 20);

        // Default to 1 paddle at bottom; co-op will add more based on connected players.
        for (int i = 0; i < _activePlayerCount; i++)
        {
            float y = vp.Height - 60 - i * 34;
            var paddlePos = new Vector2((vp.Width - _basePaddleSize.X) * 0.5f, y);
            _paddles.Add(new Paddle(paddlePos, _basePaddleSize, _preset.PaddleSpeed));

            var ball = new Ball(_paddles[i].Center - new Vector2(0, 18), radius: 8f, ownerPlayerIndex: i);
            _balls.Add(ball);
            _ballServing.Add(true);
        }
    }

    private void ResetBallOnPaddle(int ballIndex)
    {
        if ((uint)ballIndex >= (uint)_balls.Count) return;

        _ballServing[ballIndex] = true;
        _balls[ballIndex].Velocity = Vector2.Zero;

        // Attach to the owning paddle if possible.
        int desiredPaddle = Math.Clamp(_balls[ballIndex].OwnerPlayerIndex, 0, _paddles.Count - 1);
        int paddleIndex = Math.Clamp(desiredPaddle, 0, _paddles.Count - 1);
        _balls[ballIndex].Position = _paddles[paddleIndex].Center - new Vector2(0, _balls[ballIndex].Radius + 14);
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
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        dt = Math.Min(dt, 1f / 20f);

        if (_mode == WorldMode.Menu)
        {
            UpdateMenu(inputs, vp, dt);
            return;
        }

        UpdateEffects(dt);

        // Update paddles.
        for (int i = 0; i < _paddles.Count; i++)
        {
            var p = _paddles[i];
            p.SpeedPixelsPerSecond = _preset.PaddleSpeed;
            _paddles[i] = p;
        }

        ApplyPaddleSize(vp);

        for (int i = 0; i < _paddles.Count; i++)
        {
            float moveX = 0f;
            if (inputs != null && i < inputs.Length) moveX = inputs[i].MoveX;
            _paddles[i].Update(dt, moveX, vp.Width);
        }

        UpdatePowerUps(dt, vp);

        // Serve handling: each player's serve only launches their own ball(s).
        for (int i = 0; i < _balls.Count; i++)
        {
            if (_ballServing[i])
            {
                int desiredPaddle = Math.Clamp(_balls[i].OwnerPlayerIndex, 0, _paddles.Count - 1);
                int paddleIndex = Math.Clamp(desiredPaddle, 0, _paddles.Count - 1);
                _balls[i].Position = _paddles[paddleIndex].Center - new Vector2(0, _balls[i].Radius + 14);

                bool servePressed = false;
                int owner = _balls[i].OwnerPlayerIndex;
                if (inputs != null && (uint)owner < (uint)inputs.Length)
                    servePressed = inputs[owner].ServePressed;

                if (servePressed)
                    Serve(i);

                continue;
            }

            _balls[i].Update(dt);

            HandleWallCollisions(vp, _balls[i]);
            HandlePaddleCollision(_balls[i]);
            HandleBrickCollisions(_balls[i]);

            // Ball lost: decrement shared lives, but reset only this ball.
            if (_balls[i].Position.Y - _balls[i].Radius > vp.Height)
            {
                _lives--;
                if (_lives <= 0)
                {
                    _mode = WorldMode.Menu;
                    return;
                }

                ResetBallOnPaddle(i);
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
            {
                _levelIndex++;
                LoadLevel(vp, _levelIndex);
                for (int i = 0; i < _balls.Count; i++)
                    ResetBallOnPaddle(i);
            }
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
            {
                _levelIndex++;
                LoadLevel(vp, _levelIndex);
                for (int i = 0; i < _balls.Count; i++)
                    ResetBallOnPaddle(i);
            }
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
                StartNewGame(vp, Presets[_selectedPresetIndex]);
                _mode = WorldMode.Playing;
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

    private void UpdateEffects(float dt)
    {
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

    private void HandlePaddleCollision(Ball ball)
    {
        for (int pi = 0; pi < _paddles.Count; pi++)
        {
            var paddle = _paddles[pi];
            if (!ball.Bounds.Intersects(paddle.Bounds))
                continue;

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

            int points = 10;
            if (brick.HitPoints <= 0 && beforeHp > 0)
                points += 15;

            _score += (int)(points * _scoreMultiplier);

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

        int cols = 10;
        int baseRows = 5;
        int rows = Math.Min(13, baseRows + levelIndex / 2);

        int padding = 6;
        int topMargin = 70;
        int sideMargin = 30;
        int brickWidth = (vp.Width - sideMargin * 2 - padding * (cols - 1)) / cols;
        int brickHeight = 26;

        int hp = 1 + levelIndex / 3;
        hp = Math.Min(hp, _preset.MaxBrickHp);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float holeChance = MathHelper.Clamp(0.06f + levelIndex * 0.012f, 0.06f, 0.30f);
                if (_rng.NextDouble() < holeChance) continue;

                int x = sideMargin + c * (brickWidth + padding);
                int y = topMargin + r * (brickHeight + padding);
                var bounds = new Rectangle(x, y, brickWidth, brickHeight);

                int rowBonus = r / 3;
                int brickHp = Math.Min(hp + rowBonus, _preset.MaxBrickHp);

                if (IsOwnedBricksMode)
                {
                    int owner = Math.Clamp((int)((float)c / cols * _activePlayerCount), 0, _activePlayerCount - 1);
                    _bricks.Add(new Brick(bounds, brickHp, PaletteForHpOwned(brickHp, owner), ownerPlayerIndex: owner));
                }
                else
                {
                    _bricks.Add(new Brick(bounds, brickHp, PaletteForHp(brickHp)));
                }
            }
        }

        if (_bricks.Count == 0)
        {
            var bounds = new Rectangle(sideMargin, topMargin, brickWidth, brickHeight);
            if (IsOwnedBricksMode)
                _bricks.Add(new Brick(bounds, hp, PaletteForHpOwned(hp, 0), ownerPlayerIndex: 0));
            else
                _bricks.Add(new Brick(bounds, hp, PaletteForHp(hp)));
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
        if (_rng.NextDouble() > _preset.PowerUpDropChance)
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
                foreach (var ball in _balls)
                {
                    if (ball.Velocity != Vector2.Zero)
                        ball.Velocity = Vector2.Normalize(ball.Velocity) * GetTargetBallSpeedForLevel();
                }
                break;

            case PowerUpType.FastBall:
                _ballSpeedMultiplier = 1.20f;
                _ballSpeedTimeLeft = Math.Max(_ballSpeedTimeLeft, 8f);
                foreach (var ball in _balls)
                {
                    if (ball.Velocity != Vector2.Zero)
                        ball.Velocity = Vector2.Normalize(ball.Velocity) * GetTargetBallSpeedForLevel();
                }
                break;

            case PowerUpType.ExtraLife:
                _lives = Math.Min(_lives + 1, 9);
                break;

            case PowerUpType.ScoreBoost:
                _scoreMultiplier = 2f;
                _scoreMultiplierTimeLeft = Math.Max(_scoreMultiplierTimeLeft, 12f);
                break;

            case PowerUpType.MultiBall:
                SpawnMultiBall();
                break;
        }
    }

    private void SpawnMultiBall()
    {
        if (_balls.Count == 0) return;

        int sourceIndex = 0;
        for (int i = 0; i < _balls.Count; i++)
        {
            if (i < _ballServing.Count && !_ballServing[i])
            {
                sourceIndex = i;
                break;
            }
        }

        var src = _balls[sourceIndex];
        Vector2 baseDir = src.Velocity == Vector2.Zero ? new Vector2(0f, -1f) : Vector2.Normalize(src.Velocity);
        float speed = GetTargetBallSpeedForLevel();

        for (int n = 0; n < 2; n++)
        {
            float spread = MathHelper.ToRadians(n == 0 ? -18f : 18f);
            Vector2 dir = Vector2.Transform(baseDir, Matrix.CreateRotationZ(spread));

            var b = new Ball(src.Position, src.Radius, ownerPlayerIndex: src.OwnerPlayerIndex) { Velocity = dir * speed };
            _balls.Add(b);
            _ballServing.Add(false);
        }
    }

    public void Draw(SpriteBatch spriteBatch, Viewport vp)
    {
        if (_mode == WorldMode.Menu)
        {
            DrawMenu(spriteBatch, vp);
            return;
        }

        // Bricks
        for (int i = 0; i < _bricks.Count; i++)
        {
            var b = _bricks[i];
            if (!b.IsAlive) continue;

            spriteBatch.Draw(_pixel, b.Bounds, b.CurrentColor);
            var inset = new Rectangle(b.Bounds.X + 2, b.Bounds.Y + 2, Math.Max(1, b.Bounds.Width - 4), Math.Max(1, b.Bounds.Height - 4));
            spriteBatch.Draw(_pixel, inset, b.CurrentColor * 0.85f);
        }

        // Powerups
        for (int i = 0; i < _powerUps.Count; i++)
        {
            var p = _powerUps[i];
            var color = p.Type switch
            {
                PowerUpType.ExpandPaddle => new Color(90, 220, 255),
                PowerUpType.SlowBall => new Color(120, 255, 140),
                PowerUpType.FastBall => new Color(255, 140, 90),
                PowerUpType.ExtraLife => new Color(255, 90, 130),
                PowerUpType.ScoreBoost => new Color(240, 230, 90),
                PowerUpType.MultiBall => new Color(170, 240, 255),
                _ => Color.White,
            };
            spriteBatch.Draw(_pixel, p.Bounds, color);

            if (_hudFont != null)
            {
                string label = p.Type switch
                {
                    PowerUpType.ExpandPaddle => "W",
                    PowerUpType.SlowBall => "S",
                    PowerUpType.FastBall => "F",
                    PowerUpType.ExtraLife => "+",
                    PowerUpType.ScoreBoost => "2x",
                    PowerUpType.MultiBall => "MB",
                    _ => "?",
                };

                var size = _hudFont.MeasureString(label);
                spriteBatch.DrawString(_hudFont, label, new Vector2(p.Bounds.Center.X - size.X * 0.5f, p.Bounds.Center.Y - size.Y * 0.5f), Color.Black);
            }
        }

        // Paddle(s)
        for (int i = 0; i < _paddles.Count; i++)
            spriteBatch.Draw(_pixel, _paddles[i].Bounds, Color.SlateGray);

        // Ball(s)
        for (int i = 0; i < _balls.Count; i++)
            spriteBatch.Draw(_pixel, _balls[i].Bounds, Color.WhiteSmoke);

        // HUD
        if (_hudFont != null)
        {
            string text = SafeText($"DragonBreak  |  {Presets[_selectedPresetIndex].Name}  |  Level {_levelIndex + 1}  Lives {_lives}  Score {_score}  Balls {_balls.Count}");
            spriteBatch.DrawString(_hudFont, text, new Vector2(16, 14), Color.White);

            bool anyServing = false;
            for (int i = 0; i < _ballServing.Count; i++)
            {
                if (_ballServing[i]) { anyServing = true; break; }
            }

            if (anyServing)
            {
                const string prompt = "Press SPACE / A to launch";
                var size = _hudFont.MeasureString(prompt);
                spriteBatch.DrawString(_hudFont, prompt, new Vector2((vp.Width - size.X) * 0.5f, vp.Height - 110), Color.Yellow);
            }

            DrawEffectTimers(spriteBatch);
        }
    }

    private void DrawMenu(SpriteBatch spriteBatch, Viewport vp)
    {
        spriteBatch.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.85f);

        if (_hudFont == null)
            return;

        const string title = "DragonBreak";
        var titleSize = _hudFont.MeasureString(title);
        spriteBatch.DrawString(_hudFont, title, new Vector2((vp.Width - titleSize.X) * 0.5f, 70), Color.White);

        float x = vp.Width * 0.5f;
        float y = 150f;
        float lineH = 30f;

        DrawMenuLine(spriteBatch, x, y + 0 * lineH, MenuItem.GameMode, $"Mode: {_selectedGameMode}");
        DrawMenuLine(spriteBatch, x, y + 1 * lineH, MenuItem.Players, $"Players: {_selectedPlayers}");
        DrawMenuLine(spriteBatch, x, y + 2 * lineH, MenuItem.Difficulty, $"Difficulty: {Presets[_selectedPresetIndex].Name}");
        DrawMenuLine(spriteBatch, x, y + 3 * lineH, MenuItem.Start, "Start");

        const string hint = "UP/DOWN to select - LEFT/RIGHT to change - ENTER/SPACE/A to confirm";
        var hintSize = _hudFont.MeasureString(SafeText(hint));
        spriteBatch.DrawString(_hudFont, SafeText(hint), new Vector2((vp.Width - hintSize.X) * 0.5f, vp.Height - 90), Color.LightGray);
    }

    private void DrawMenuLine(SpriteBatch spriteBatch, float centerX, float y, MenuItem item, string text)
    {
        if (_hudFont == null) return;

        bool selected = item == _menuItem;
        string line = selected ? $"> {text}" : $"  {text}";
        line = SafeText(line);
        var size = _hudFont.MeasureString(line);
        spriteBatch.DrawString(_hudFont, line, new Vector2(centerX - size.X * 0.5f, y), selected ? Color.Yellow : Color.White);
    }

    private void DrawEffectTimers(SpriteBatch spriteBatch)
    {
        if (_hudFont == null) return;

        float x = 16;
        float y = 40;

        if (_paddleWidthTimeLeft > 0f)
        {
            var s = SafeText($"WIDE {Math.Ceiling(_paddleWidthTimeLeft)}");
            spriteBatch.DrawString(_hudFont, s, new Vector2(x, y), new Color(90, 220, 255));
            y += 20;
        }

        if (_ballSpeedTimeLeft > 0f)
        {
            string label = _ballSpeedMultiplier < 1f ? "SLOW" : "FAST";
            var s = SafeText($"{label} {Math.Ceiling(_ballSpeedTimeLeft)}");
            spriteBatch.DrawString(_hudFont, s, new Vector2(x, y), Color.White);
            y += 20;
        }

        if (_scoreMultiplierTimeLeft > 0f)
        {
            var s = SafeText($"2X {Math.Ceiling(_scoreMultiplierTimeLeft)}");
            spriteBatch.DrawString(_hudFont, s, new Vector2(x, y), new Color(240, 230, 90));
        }
    }
}
