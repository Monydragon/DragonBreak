#nullable enable
using System;
using System.Collections.Generic;
using DragonBreak.Core.Breakout;
using DragonBreak.Core.Settings;

namespace DragonBreak.Core.Highscores;

public sealed class HighScoreService
{
    private readonly IHighScoreStore _store;
    private HighScoreDatabase _db;

    public HighScoreSettings Settings => _db.Settings;

    public HighScoreService(IHighScoreStore store)
    {
        _store = store;
        _db = _store.LoadOrDefault();
    }

    public void Reload() => _db = _store.LoadOrDefault();

    public void SetMaxEntriesPerKey(int max)
    {
        _db = _db with { Settings = (_db.Settings ?? HighScoreSettings.Default) with { MaxEntriesPerKey = max } };
        _db = _db.Validate();
        _store.Save(_db);
    }

    private static string KeyString(GameModeId mode, DifficultyId diff)
        => new HighScoreKey(mode, diff).ToString();

    public IReadOnlyList<HighScoreEntry> GetTop(GameModeId mode, DifficultyId difficulty)
    {
        string k = KeyString(mode, difficulty);
        if (_db.Tables.TryGetValue(k, out var list) && list != null)
            return list;

        return Array.Empty<HighScoreEntry>();
    }

    public IReadOnlyList<HighScoreEntry> GetPage(GameModeId mode, DifficultyId difficulty, int offset, int count)
    {
        var all = GetTop(mode, difficulty);
        if (offset < 0) offset = 0;
        if (count <= 0) return Array.Empty<HighScoreEntry>();
        if (offset >= all.Count) return Array.Empty<HighScoreEntry>();

        int take = Math.Min(count, all.Count - offset);
        var page = new HighScoreEntry[take];
        for (int i = 0; i < take; i++)
            page[i] = all[offset + i];
        return page;
    }

    public bool WouldQualify(HighScoreEntry entry)
    {
        if (entry == null) return false;
        if (entry.Score <= 0) return false;

        var list = GetTop(entry.Mode, entry.Difficulty);
        int max = (_db.Settings ?? HighScoreSettings.Default).Validate().MaxEntriesPerKey;

        if (list.Count < max) return true;

        // List is sorted desc. Qualify if strictly greater than last.
        var last = list[^1];
        if (entry.Score > last.Score) return true;
        if (entry.Score == last.Score && entry.LevelReached > last.LevelReached) return true;
        return false;
    }

    public bool TrySubmit(HighScoreEntry entry)
    {
        if (entry == null) return false;

        string name = (entry.Name ?? "").Trim();
        if (name.Length == 0) name = "PLAYER";
        if (name.Length > 12) name = name[..12];

        var cleaned = entry with { Name = name, Score = Math.Max(0, entry.Score), LevelReached = Math.Max(0, entry.LevelReached) };

        if (!WouldQualify(cleaned))
            return false;

        string k = KeyString(cleaned.Mode, cleaned.Difficulty);

        if (!_db.Tables.TryGetValue(k, out var list) || list == null)
            list = new List<HighScoreEntry>();

        list.Add(cleaned);
        _db.Tables[k] = list;

        _db = _db.Validate();
        _store.Save(_db);
        return true;
    }
}

