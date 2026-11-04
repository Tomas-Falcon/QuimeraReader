using System;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;

namespace QuimeraReader.Desktop.Services;

public static class DesktopPreferences
{
    private static readonly string _settingsFilePath = Path.Combine(DesktopStorage.AppDataDirectory, "preferences.json");
    private static readonly ConcurrentDictionary<string, string> _values = new();
    private static readonly object _fileLock = new();

    static DesktopPreferences()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _values[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch { }
    }

    public static string Get(string key, string defaultValue)
    {
        return _values.TryGetValue(key, out var val) ? val : defaultValue;
    }

    public static bool Get(string key, bool defaultValue)
    {
        return _values.TryGetValue(key, out var val) && bool.TryParse(val, out var res) ? res : defaultValue;
    }

    public static int Get(string key, int defaultValue)
    {
        return _values.TryGetValue(key, out var val) && int.TryParse(val, out var res) ? res : defaultValue;
    }

    public static void Set(string key, string value)
    {
        _values[key] = value;
        Save();
    }

    public static void Set(string key, bool value)
    {
        _values[key] = value.ToString();
        Save();
    }

    public static void Set(string key, int value)
    {
        _values[key] = value.ToString();
        Save();
    }

    private static void Save()
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }
    }
}
