using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

/// <summary>
/// Expone las descargas offline al WebView como rutas del propio origen (<see cref="MediaProxy.LocalPathPrefix"/>)
/// que <see cref="MediaProxyInterceptor"/> sirve desde disco. Solo donde el interceptor está suscrito (Android y Windows).
/// </summary>
public class MobileLocalMediaServer : ILocalMediaServer
{
    public string? GetUrl(string localFilePath)
    {
        if (!(OperatingSystem.IsAndroid() || OperatingSystem.IsWindows())) return null;
        if (string.IsNullOrEmpty(localFilePath) || !File.Exists(localFilePath)) return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(localFilePath));
        if (!string.Equals(directory, Path.GetFullPath(FileSystem.AppDataDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return MediaProxy.LocalPathPrefix + Uri.EscapeDataString(Path.GetFileName(localFilePath));
    }
}
