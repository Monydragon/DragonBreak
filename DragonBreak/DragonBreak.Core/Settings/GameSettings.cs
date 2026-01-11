#nullable enable
namespace DragonBreak.Core.Settings;

public sealed record GameSettings
{
    public DisplaySettings Display { get; init; } = DisplaySettings.Default;
    public AudioSettings Audio { get; init; } = AudioSettings.Default;

    public GameplaySettings Gameplay { get; init; } = GameplaySettings.Default;
    public UiSettings Ui { get; init; } = UiSettings.Default;

    public static GameSettings Default => new()
    {
        Display = DisplaySettings.Default,
        Audio = AudioSettings.Default,
        Gameplay = GameplaySettings.Default,
        Ui = UiSettings.Default,
    };

    public GameSettings Validate()
        => this with
        {
            Display = (Display ?? DisplaySettings.Default).Validate(),
            Audio = AudioSettings.Default with
            {
                MasterVolume = Audio.MasterVolume,
                BgmVolume = Audio.BgmVolume,
                SfxVolume = Audio.SfxVolume,
            },
            Gameplay = (Gameplay ?? GameplaySettings.Default).Validate(),
            Ui = (Ui ?? UiSettings.Default).Validate(),
        };
}
