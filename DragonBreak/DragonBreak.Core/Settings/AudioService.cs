using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Media;

namespace DragonBreak.Core.Settings;

public sealed class AudioService
{
    public void Apply(AudioSettings audio)
    {
        // MonoGame uses separate global controls for SFX and MediaPlayer.
        // We apply master into both, and keep the user's BGM/SFX split.
        float master = AudioSettings.Clamp01(audio.MasterVolume);
        float bgm = AudioSettings.Clamp01(audio.BgmVolume);
        float sfx = AudioSettings.Clamp01(audio.SfxVolume);

        MediaPlayer.Volume = master * bgm;
        SoundEffect.MasterVolume = master * sfx;
    }
}

