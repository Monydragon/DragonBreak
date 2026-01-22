#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using DragonBreak.Core.Breakout;
using DragonBreak.Core.Highscores;
using DragonBreak.Core.Input;
using DragonBreak.Core.Localization;
using DragonBreak.Core.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DragonBreak.Core
{
    /// <summary>
    /// The main class for the game, responsible for managing game components, settings, 
    /// and platform-specific configurations.
    /// </summary>
    public class DragonBreakGame : Game
    {
        // Resources for drawing.
        private GraphicsDeviceManager graphicsDeviceManager;

        private SpriteBatch _spriteBatch = null!;
        private readonly InputMapper _input = new();
        private readonly BreakoutWorld _world = new();

        /// <summary>
        /// Optional platform hook to provide current multitouch input.
        /// If null, the game uses keyboard/gamepad only.
        /// </summary>
        public Func<Viewport, TouchState>? TouchInjector { get; set; }

        // Cache rasterizer states (avoid allocating per-frame).
        private static readonly RasterizerState RasterScissorOn = new() { ScissorTestEnable = true };
        private static readonly RasterizerState RasterScissorOff = new() { ScissorTestEnable = false };

        private readonly SettingsStore _settingsStore;
        private readonly SettingsManager _settings;
        private readonly DisplayModeService _displayModes;
        private readonly AudioService _audio;
        private readonly HighScoreService _highscores;

        /// <summary>
        /// Indicates if the game is running on a mobile platform.
        /// </summary>
        public readonly static bool IsMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        /// <summary>
        /// Indicates if the game is running on a desktop platform.
        /// </summary>
        public readonly static bool IsDesktop =
            OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        /// <summary>
        /// Initializes a new instance of the game. Configures platform-specific settings, 
        /// initializes services like settings and leaderboard managers, and sets up the 
        /// screen manager for screen transitions.
        /// </summary>
        public DragonBreakGame()
        {
            graphicsDeviceManager = new GraphicsDeviceManager(this);

            // Share GraphicsDeviceManager as a service.
            Services.AddService(typeof(GraphicsDeviceManager), graphicsDeviceManager);

            _settingsStore = new SettingsStore();
            _settings = new SettingsManager(_settingsStore);
            _displayModes = new DisplayModeService();
            _audio = new AudioService();

            // Highscores (local JSON for now)
            _highscores = new HighScoreService(new JsonHighScoreStore("DragonBreak"));

            Services.AddService(typeof(SettingsManager), _settings);
            Services.AddService(typeof(DisplayModeService), _displayModes);
            Services.AddService(typeof(AudioService), _audio);
            Services.AddService(typeof(HighScoreService), _highscores);

            _settings.SettingsApplied += ApplySettings;

            Content.RootDirectory = "Content";

            // Configure screen orientations.
            graphicsDeviceManager.SupportedOrientations =
                DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;

            if (IsDesktop)
            {
                Window.Title = "DragonBreak";
                IsMouseVisible = true;

                // We'll toggle this based on window mode when applying settings.
                Window.AllowUserResizing = true;
                Window.ClientSizeChanged += (_, _) => OnClientSizeChanged();
            }
        }

        /// <summary>
        /// Initializes the game, including setting up localization and adding the 
        /// initial screens to the ScreenManager.
        /// </summary>
        protected override void Initialize()
        {
            // Apply settings as early as possible.
            ApplySettings(_settings.Current);

            // Forward text input (keyboard typing) to the world for name entry.
            // Some platform builds (e.g., Android) don't expose Window.TextInput.
#if !ANDROID && !IOS
            Window.TextInput += (_, e) => _world.OnTextInput(e.Character);
#endif

            base.Initialize();

            // Load supported languages and set the default language.
            List<CultureInfo> cultures = LocalizationManager.GetSupportedCultures();
            var languages = new List<CultureInfo>();
            for (int i = 0; i < cultures.Count; i++)
            {
                languages.Add(cultures[i]);
            }

            // TODO You should load this from a settings file or similar,
            // based on what the user or operating system selected.
            var selectedLanguage = LocalizationManager.DEFAULT_CULTURE_CODE;
            LocalizationManager.SetCulture(selectedLanguage);
        }

        private void ApplySettings(GameSettings settings)
        {
            settings = (settings ?? GameSettings.Default).Validate();

            // Display
            _displayModes.Apply(settings.Display, graphicsDeviceManager, Window);

            // Audio
            _audio.Apply(settings.Audio);
        }

        private void OnClientSizeChanged()
        {
            if (!IsDesktop)
                return;

            // Only persist user resizing when in windowed mode.
            var current = _settings.Current;
            if (current.Display.WindowMode != WindowMode.Windowed)
                return;

            int w = Window.ClientBounds.Width;
            int h = Window.ClientBounds.Height;
            if (w <= 0 || h <= 0)
                return;

            // Avoid churn if the size hasn't actually changed.
            if (current.Display.Width == w && current.Display.Height == h)
                return;

            _settings.UpdateCurrent(current with { Display = current.Display with { Width = w, Height = h } }, save: true);
        }

        /// <summary>
        /// Loads game content, such as textures and particle systems.
        /// </summary>
        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _world.Load(GraphicsDevice, Content, Services);
            base.LoadContent();
        }

        /// <summary>
        /// Updates the game's logic, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for game updates.
        /// </param>
        protected override void Update(GameTime gameTime)
        {
            // Gather inputs for up to 4 local players (controllers). Keyboard is shared.
            var input1 = _input.Update(PlayerIndex.One);
            var input2 = _input.UpdateForPlayer(PlayerIndex.Two);
            var input3 = _input.UpdateForPlayer(PlayerIndex.Three);
            var input4 = _input.UpdateForPlayer(PlayerIndex.Four);

            // Mobile: inject pointer/touches into player 1.
            if (TouchInjector != null)
            {
                input1 = input1.WithTouches(TouchInjector(GraphicsDevice.Viewport));
            }

            // Exit the game if requested.
            if (input1.ExitPressed || input2.ExitPressed || input3.ExitPressed || input4.ExitPressed)
                Exit();

            _world.Update(gameTime, new[] { input1, input2, input3, input4 }, GraphicsDevice.Viewport);

            base.Update(gameTime);
        }

        /// <summary>
        /// Draws the game's graphics, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for rendering.
        /// </param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            var full = GraphicsDevice.Viewport;

            // Keep in sync with BreakoutWorld's HUD bar height.
            int hudH = Math.Clamp(96, 0, Math.Max(0, full.Height - 1));
            var playfieldRect = new Rectangle(full.X, full.Y + hudH, full.Width, Math.Max(1, full.Height - hudH));

            // Pass 1: gameplay clipped to the playfield (below the top HUD bar).
            var prevScissor = GraphicsDevice.ScissorRectangle;
            GraphicsDevice.ScissorRectangle = playfieldRect;

            // Translate the gameplay coordinate system so (0,0) is the top-left of the playfield.
            // Without this, gameplay is drawn at y=0 and can appear to overlap the HUD region.
            var gameplayTransform = Matrix.CreateTranslation(0f, hudH, 0f);

            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp,
                rasterizerState: RasterScissorOn,
                transformMatrix: gameplayTransform);

            _world.Draw(_spriteBatch, full);
            _spriteBatch.End();

            // Restore scissor.
            GraphicsDevice.ScissorRectangle = prevScissor;

            // Pass 2: UI (HUD/top bar/menus) un-clipped.
            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp,
                rasterizerState: RasterScissorOff);

            _world.DrawUi(_spriteBatch, full);
            _spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
