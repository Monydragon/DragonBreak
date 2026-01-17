#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DragonBreak.Core.Highscores;

public interface IHighScoreStore
{
    HighScoreDatabase LoadOrDefault();
    void Save(HighScoreDatabase db);

    string Path { get; }
}

public sealed class JsonHighScoreStore : IHighScoreStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Path { get; }

    public JsonHighScoreStore(string? appName = null)
    {
        appName ??= "DragonBreak";

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = System.IO.Path.Combine(root, appName);
        Directory.CreateDirectory(dir);

        Path = System.IO.Path.Combine(dir, "highscores.json");
    }

    public HighScoreDatabase LoadOrDefault()
    {
        try
        {
            if (!File.Exists(Path))
                return HighScoreDatabase.CreateDefault();

            var json = File.ReadAllText(Path);
            var db = JsonSerializer.Deserialize<HighScoreDatabase>(json, _jsonOptions);
            return (db ?? HighScoreDatabase.CreateDefault()).Validate();
        }
        catch
        {
            return HighScoreDatabase.CreateDefault();
        }
    }

    public void Save(HighScoreDatabase db)
    {
        db ??= HighScoreDatabase.CreateDefault();
        var validated = db.Validate();

        var json = JsonSerializer.Serialize(validated, _jsonOptions);

        // Atomic-ish write: write temp then move.
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, json);

        try
        {
            if (File.Exists(Path))
                File.Replace(tmp, Path, destinationBackupFileName: null);
            else
                File.Move(tmp, Path);
        }
        catch
        {
            // Fallback if File.Replace isn't available on some platforms.
            File.Copy(tmp, Path, overwrite: true);
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }
}

public sealed record HighScoreDatabase
{
    public int SchemaVersion { get; init; } = 1;
    public HighScoreSettings Settings { get; init; } = HighScoreSettings.Default;

    /// <summary>
    /// Map key string ("Mode/Difficulty") -> sorted descending list.
    /// </summary>
    public Dictionary<string, List<HighScoreEntry>> Tables { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static HighScoreDatabase CreateDefault()
        => new()
        {
            SchemaVersion = 1,
            Settings = HighScoreSettings.Default,
            Tables = new Dictionary<string, List<HighScoreEntry>>(StringComparer.OrdinalIgnoreCase),
        };

    public HighScoreDatabase Validate()
    {
        var validatedSettings = (Settings ?? HighScoreSettings.Default).Validate();

        var tables = Tables ?? new Dictionary<string, List<HighScoreEntry>>(StringComparer.OrdinalIgnoreCase);
        var outTables = new Dictionary<string, List<HighScoreEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in tables)
        {
            var list = kv.Value ?? new List<HighScoreEntry>();

            // Filter invalid / sanitize names.
            var cleaned = new List<HighScoreEntry>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null) continue;

                string name = (e.Name ?? "").Trim();
                if (name.Length == 0) name = "PLAYER";
                if (name.Length > 12) name = name[..12];

                cleaned.Add(e with
                {
                    Name = name,
                    Score = Math.Max(0, e.Score),
                    LevelReached = Math.Max(0, e.LevelReached),
                });
            }

            cleaned.Sort((a, b) =>
            {
                int s = b.Score.CompareTo(a.Score);
                if (s != 0) return s;
                // Higher level reached breaks ties.
                int l = b.LevelReached.CompareTo(a.LevelReached);
                if (l != 0) return l;
                // Earlier timestamp wins ties (stable-ish).
                return a.Timestamp.CompareTo(b.Timestamp);
            });

            if (cleaned.Count > validatedSettings.MaxEntriesPerKey)
                cleaned.RemoveRange(validatedSettings.MaxEntriesPerKey, cleaned.Count - validatedSettings.MaxEntriesPerKey);

            outTables[kv.Key] = cleaned;
        }

        return this with
        {
            SchemaVersion = 1,
            Settings = validatedSettings,
            Tables = outTables,
        };
    }
}

