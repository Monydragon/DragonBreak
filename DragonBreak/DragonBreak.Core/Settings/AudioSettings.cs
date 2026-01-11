#nullable enable
using System;

namespace DragonBreak.Core.Settings;

public sealed record AudioSettings
{
    private float _masterVolume = 1f;
    private float _bgmVolume = 1f;
    private float _sfxVolume = 1f;

    /// <summary>Master volume in [0..1]. Applied to BGM and SFX.</summary>
    public float MasterVolume
    {
        get => _masterVolume;
        init => _masterVolume = Clamp01(value);
    }

    /// <summary>BGM volume in [0..1]. Effective BGM = Master * Bgm.</summary>
    public float BgmVolume
    {
        get => _bgmVolume;
        init => _bgmVolume = Clamp01(value);
    }

    /// <summary>SFX volume in [0..1]. Effective SFX = Master * Sfx.</summary>
    public float SfxVolume
    {
        get => _sfxVolume;
        init => _sfxVolume = Clamp01(value);
    }

    public static AudioSettings Default => new()
    {
        MasterVolume = 1f,
        BgmVolume = 0.8f,
        SfxVolume = 0.9f,
    };

    internal static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
}
