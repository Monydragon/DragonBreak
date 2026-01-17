#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Input;
using DragonBreak.Core.Settings;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout.Ui;

internal sealed class NameEntryScreen : IBreakoutScreen
{
    internal enum NameEntryAction
    {
        None,
        Submitted,
        Canceled,
    }

    private const int NameMaxLen = 12;

    private int _playerIndex;
    private int _finalScore;
    private GameModeId _mode;
    private DifficultyId _difficulty;
    private int _levelReached;
    private int _seed;

    private string _name = string.Empty;

    // Controller picker
    private static readonly char[] PickerChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();
    private int _pickerIndex;

    private bool _leftConsumed;
    private bool _rightConsumed;
    private bool _upConsumed;
    private bool _downConsumed;

    private NameEntryAction _pending;

    public void Show(int playerIndex, int finalScore, GameModeId mode, DifficultyId difficulty, int levelReached, int seed)
    {
        _playerIndex = Math.Max(0, playerIndex);
        _finalScore = Math.Max(0, finalScore);
        _mode = mode;
        _difficulty = difficulty;
        _levelReached = Math.Max(0, levelReached);
        _seed = seed;

        _name = string.Empty;
        _pickerIndex = 0;

        _pending = NameEntryAction.None;

        _leftConsumed = _rightConsumed = _upConsumed = _downConsumed = false;
    }

    public string GetSubmittedName()
    {
        var trimmed = _name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return $"PLAYER {_playerIndex + 1}";

        return trimmed;
    }

    public (int FinalScore, GameModeId Mode, DifficultyId Difficulty, int LevelReached, int Seed, int PlayerIndex) GetContext()
        => (_finalScore, _mode, _difficulty, _levelReached, _seed, _playerIndex);

    public NameEntryAction ConsumeAction()
    {
        var a = _pending;
        _pending = NameEntryAction.None;
        return a;
    }

    public void OnTextInput(char c)
    {
        if (_pending != NameEntryAction.None)
            return;

        // Backspace
        if (c == '\b')
        {
            Backspace();
            return;
        }

        if (c == '\r' || c == '\n')
        {
            Submit();
            return;
        }

        if (!IsAllowedChar(c))
            return;

        if (_name.Length >= NameMaxLen)
            return;

        // Normalize to uppercase for consistency.
        _name += char.ToUpperInvariant(c);
    }

    private static bool IsAllowedChar(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

    private void Backspace()
    {
        if (string.IsNullOrEmpty(_name))
            return;

        _name = _name[..^1];
    }

    private void Submit()
    {
        _pending = NameEntryAction.Submitted;
    }

    public void Update(DragonBreakInput[] inputs, Viewport vp, float dtSeconds)
    {
        bool confirmPressed = false;
        bool backPressed = false;

        bool leftHeldAny = false;
        bool rightHeldAny = false;
        bool upHeldAny = false;
        bool downHeldAny = false;

        float mx = 0f;
        float my = 0f;

        for (int i = 0; i < inputs.Length; i++)
        {
            confirmPressed |= inputs[i].MenuConfirmPressed || inputs[i].ServePressed;
            backPressed |= inputs[i].MenuBackPressed;

            leftHeldAny |= inputs[i].MenuLeftHeld;
            rightHeldAny |= inputs[i].MenuRightHeld;
            upHeldAny |= inputs[i].MenuUpHeld;
            downHeldAny |= inputs[i].MenuDownHeld;

            if (Math.Abs(inputs[i].MenuMoveX) > Math.Abs(mx)) mx = inputs[i].MenuMoveX;
            if (Math.Abs(inputs[i].MenuMoveY) > Math.Abs(my)) my = inputs[i].MenuMoveY;
        }

        const float deadzone = 0.55f;
        bool leftHeld = leftHeldAny || mx <= -deadzone;
        bool rightHeld = rightHeldAny || mx >= deadzone;
        bool upHeld = upHeldAny || my >= deadzone;
        bool downHeld = downHeldAny || my <= -deadzone;

        // Controller picker: Up/Down cycles characters.
        if (upHeld && !_upConsumed)
        {
            _pickerIndex = (_pickerIndex + 1) % PickerChars.Length;
            _upConsumed = true;
        }
        if (!upHeld) _upConsumed = false;

        if (downHeld && !_downConsumed)
        {
            _pickerIndex = (_pickerIndex - 1) % PickerChars.Length;
            if (_pickerIndex < 0) _pickerIndex += PickerChars.Length;
            _downConsumed = true;
        }
        if (!downHeld) _downConsumed = false;

        // Quick navigation: shoulder buttons / left-right to add/remove.
        if (rightHeld && !_rightConsumed)
        {
            AppendPickerChar();
            _rightConsumed = true;
        }
        if (!rightHeld) _rightConsumed = false;

        if (leftHeld && !_leftConsumed)
        {
            Backspace();
            _leftConsumed = true;
        }
        if (!leftHeld) _leftConsumed = false;

        // Confirm submits.
        if (confirmPressed)
        {
            Submit();
            return;
        }

        // Back cancels (skip name).
        if (backPressed)
        {
            _pending = NameEntryAction.Canceled;
        }
    }

    private void AppendPickerChar()
    {
        if (_name.Length >= NameMaxLen)
            return;

        _name += PickerChars[Math.Clamp(_pickerIndex, 0, PickerChars.Length - 1)];
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        // Drawn by BreakoutWorld.
    }

    public IEnumerable<(string Text, bool Selected)> GetLines(Viewport vp)
    {
        yield return ("ENTER YOUR NAME", false);
        yield return ($"MODE: {_mode}   DIFF: {_difficulty}", false);
        yield return ($"SCORE: {_finalScore,7}   LEVEL: {_levelReached + 1}", false);
        yield return ("", false);

        // Inline fixed-width field with cursor; shows exactly what will be submitted.
        string shown = _name;
        if (string.IsNullOrWhiteSpace(shown))
            shown = $"PLAYER {_playerIndex + 1}";

        string cursor = _name.Length < NameMaxLen ? "_" : "";
        yield return ($"> {shown}{cursor}", false);
        yield return ($"({_name.Length}/{NameMaxLen})  A-Z / 0-9 only", false);
        yield return ("", false);

        yield return ("Keyboard: type  | Backspace=delete | Enter=OK | Esc=skip", false);
        yield return ($"Controller: Up/Down pick [{PickerChars[_pickerIndex]}] | Right=add | Left=delete", false);
    }
}
