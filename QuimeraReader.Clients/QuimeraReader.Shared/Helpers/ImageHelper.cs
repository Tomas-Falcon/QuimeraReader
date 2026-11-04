using System;
using System.Collections.Concurrent;
using System.IO;

namespace QuimeraReader.Shared.Helpers
{
    public static class ImageHelper
    {
        private static readonly ConcurrentDictionary<string, (DateTime LastWriteUtc, string DataUrl)> _localCoverCache = new();

        public static string GetImageSource(string? coverUrl, string baseAddress)
        {
            if (string.IsNullOrEmpty(coverUrl)) return "";
            if (coverUrl.StartsWith("http") || coverUrl.StartsWith("data:") || coverUrl.StartsWith("blob:")) return coverUrl;
            
            // Check if it looks like a local path (has slashes and exists)
            if ((coverUrl.StartsWith("/") || coverUrl.Contains(":\\")) && File.Exists(coverUrl))
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(coverUrl);
                    if (_localCoverCache.TryGetValue(coverUrl, out var cached) && cached.LastWriteUtc == lastWrite)
                    {
                        return cached.DataUrl;
                    }

                    // Prevent unbounded memory growth
                    if (_localCoverCache.Count > 300)
                    {
                        _localCoverCache.Clear();
                    }

                    var bytes = File.ReadAllBytes(coverUrl);
                    var base64 = Convert.ToBase64String(bytes);
                    var ext = Path.GetExtension(coverUrl).TrimStart('.').ToLowerInvariant();
                    if (ext == "jpg" || string.IsNullOrEmpty(ext)) ext = "jpeg";
                    var dataUrl = "data:image/" + ext + ";base64," + base64;

                    _localCoverCache[coverUrl] = (lastWrite, dataUrl);
                    return dataUrl;
                }
                catch { }
            }
            
            var baseUri = baseAddress.TrimEnd('/');
            var relative = coverUrl.TrimStart('/');
            return $"{baseUri}/{relative}";
        }

        public static void InvalidateCache(string? localPath = null)
        {
            if (string.IsNullOrEmpty(localPath))
                _localCoverCache.Clear();
            else
                _localCoverCache.TryRemove(localPath, out _);
        }
    }
}
