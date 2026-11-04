using System;
using System.IO;

namespace QuimeraReader.Desktop.Services;

public static class DesktopStorage
{
    private static readonly string _appDataDir;

    static DesktopStorage()
    {
        // En Linux: ~/.local/share/quimerareader
        // En Windows: %LocalAppData%\quimerareader
        // En macOS: ~/Library/Application Support/quimerareader
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _appDataDir = Path.Combine(basePath, "quimerareader");
        if (!Directory.Exists(_appDataDir))
        {
            Directory.CreateDirectory(_appDataDir);
        }
    }

    public static string AppDataDirectory => _appDataDir;
    public static string DatabasePath => Path.Combine(_appDataDir, "quimerareader_local.db");
}
