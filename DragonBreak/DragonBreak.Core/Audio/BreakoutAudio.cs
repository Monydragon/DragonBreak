#nullable enable
using System;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Media;

namespace DragonBreak.Core.Audio;

/// <summary>
/// Loads and plays audio used by the Breakout mode.
/// 
/// Contract:
/// - Content names are MonoGame content names (path without extension).
/// - Music is played via <see cref="MediaPlayer"/> so it respects <see cref="DragonBreak.Core.Settings.AudioService"/>.
/// - SFX are played via <see cref="SoundEffect"/> and may be randomized.
/// </summary>
public sealed class BreakoutAudio
{
    private readonly Random _rng = new();

    private Song? _bgm;

    private SoundEffect[] _brickHit = Array.Empty<SoundEffect>();
    private SoundEffect[] _brickBreak = Array.Empty<SoundEffect>();

    // Simple spam guard (brick collisions can happen many frames in a row).
    private TimeSpan _lastBrickSfxAt = TimeSpan.Zero;
    private readonly TimeSpan _brickSfxCooldown = TimeSpan.FromMilliseconds(35);

    private SoundEffect? _brickBreak1;

    public void Load(ContentManager content)
    {
        // Music
        _bgm = content.Load<Song>("Audio/Music/lofi-city");

        // Single explicit SFX requested by gameplay.
        try
        {
            _brickBreak1 = content.Load<SoundEffect>("Audio/SFX/brick-break-1");
        }
        catch
        {
            _brickBreak1 = null;
        }

        // SFX (optional: project will still run if you haven't added them yet)
        _brickHit = LoadOptionalSet(content,
            "Audio/SFX/Bricks/brick_hit_01",
            "Audio/SFX/Bricks/brick_hit_02",
            "Audio/SFX/Bricks/brick_hit_03",
            "Audio/SFX/Bricks/brick_hit_04",
            "Audio/SFX/Bricks/brick_hit_05",
            "Audio/SFX/Bricks/brick_hit_06");

        _brickBreak = LoadOptionalSet(content,
            "Audio/SFX/Bricks/brick_break_01",
            "Audio/SFX/Bricks/brick_break_02",
            "Audio/SFX/Bricks/brick_break_03",
            "Audio/SFX/Bricks/brick_break_04");
    }

    public void PlayBgmLoop()
    {
        if (_bgm == null)
            return;

        if (MediaPlayer.Queue.ActiveSong == _bgm && MediaPlayer.State == MediaState.Playing)
            return;

        MediaPlayer.IsRepeating = true;
        MediaPlayer.Play(_bgm);
    }

    public void StopBgm()
    {
        if (MediaPlayer.State != MediaState.Stopped)
            MediaPlayer.Stop();
    }

    public void OnBrickHit(TimeSpan totalGameTime)
    {
        if (!CanPlayBrickSfx(totalGameTime))
            return;

        PlayRandom(_brickHit);
        _lastBrickSfxAt = totalGameTime;
    }

    public void OnBrickBreak(TimeSpan totalGameTime)
    {
        if (!CanPlayBrickSfx(totalGameTime))
            return;

        PlayRandom(_brickBreak);
        _lastBrickSfxAt = totalGameTime;
    }

    public void PlayBrickBreak1(TimeSpan totalGameTime)
    {
        if (!CanPlayBrickSfx(totalGameTime))
            return;

        if (_brickBreak1 != null)
        {
            var instance = _brickBreak1.CreateInstance();
            instance.Pitch = 0f; // consistent
            instance.Play();
            _lastBrickSfxAt = totalGameTime;
        }
    }

    private bool CanPlayBrickSfx(TimeSpan totalGameTime)
    {
        // If user hasn't provided files yet, arrays are empty.
        if ((_brickHit.Length == 0) && (_brickBreak.Length == 0) && _brickBreak1 == null)
            return false;

        return (totalGameTime - _lastBrickSfxAt) >= _brickSfxCooldown;
    }

    private void PlayRandom(SoundEffect[] set)
    {
        if (set.Length == 0)
            return;

        int i = _rng.Next(set.Length);

        // Use instances so overlapping hits don’t cut each other off.
        var instance = set[i].CreateInstance();

        // Subtle pitch variance for variety.
        instance.Pitch = (float)(_rng.NextDouble() * 0.08 - 0.04); // [-0.04..+0.04]
        instance.Play();
    }

    private static SoundEffect[] LoadOptionalSet(ContentManager content, params string[] assetNames)
    {
        var list = new System.Collections.Generic.List<SoundEffect>(assetNames.Length);

        for (int i = 0; i < assetNames.Length; i++)
        {
            try
            {
                list.Add(content.Load<SoundEffect>(assetNames[i]));
            }
            catch
            {
                // Missing content is okay during setup.
            }
        }

        return list.ToArray();
    }
}
