#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace DragonBreak.Core.Settings;

public sealed class SettingsStore
{
    private const string SettingsFileName = "settings.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string SettingsPath { get; }

    public SettingsStore(string? appName = null)
    {
        // Desktop defaults. Mobile can override later if needed.
        appName ??= "DragonBreak";

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, appName);
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, SettingsFileName);
    }

    public GameSettings LoadOrDefault()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return GameSettings.Default;

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<GameSettings>(json, _jsonOptions);
            return (settings ?? GameSettings.Default).Validate();
        }
        catch
        {
            // If something goes wrong (corrupt json, etc.), fall back safely.
            return GameSettings.Default;
        }
    }

    public void Save(GameSettings settings)
    {
        var validated = (settings ?? GameSettings.Default).Validate();
        var json = JsonSerializer.Serialize(validated, _jsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
