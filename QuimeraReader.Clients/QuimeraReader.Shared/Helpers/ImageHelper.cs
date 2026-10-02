using System;
using System.IO;

namespace QuimeraReader.Shared.Helpers
{
    public static class ImageHelper
    {
        public static string GetImageSource(string? coverUrl, string baseAddress)
        {
            if (string.IsNullOrEmpty(coverUrl)) return "";
            
            // Check if it looks like a local path (has slashes and exists)
            if ((coverUrl.StartsWith("/") || coverUrl.Contains(":\\")) && File.Exists(coverUrl))
            {
                try
                {
                    var bytes = File.ReadAllBytes(coverUrl);
                    var base64 = Convert.ToBase64String(bytes);
                    var ext = Path.GetExtension(coverUrl).TrimStart('.').ToLower();
                    if (ext == "jpg") ext = "jpeg";
                    return "data:image/" + ext + ";base64," + base64;
                }
                catch { }
            }
            
            if (coverUrl.StartsWith("http") || coverUrl.StartsWith("data:")) return coverUrl;
            
            var baseUri = baseAddress.TrimEnd('/');
            var relative = coverUrl.TrimStart('/');
            return $"{baseUri}/{relative}";
        }
    }
}

