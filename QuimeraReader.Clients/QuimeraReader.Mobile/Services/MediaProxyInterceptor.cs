using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

/// <summary>
/// Atiende las peticiones a <see cref="MediaProxy.PathPrefix"/> del WebView reenviándolas al servidor con el HttpClient nativo.
/// Evita el bloqueo por "mixed content" (la app corre en https://0.0.0.1 y el servidor suele ser http://) y añade la API key
/// en cabecera. Reenvía Range para que el audio pueda hacer streaming y saltos (206 Partial Content).
/// </summary>
public static class MediaProxyInterceptor
{
    // Sin timeout global: un audiolibro puede tardar en descargarse; la cancelación la gestiona el propio WebView
    private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly string[] ForwardedResponseHeaders =
    {
        "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "Cache-Control", "Last-Modified", "ETag"
    };

    public static void OnWebResourceRequested(object? sender, WebViewWebResourceRequestedEventArgs e)
    {
        if (!e.Uri.AbsolutePath.StartsWith(MediaProxy.PathPrefix, StringComparison.Ordinal)) return;

        e.Handled = true;

        var serverUrl = Preferences.Default.Get(MobileServerConfigService.ServerUrlKey, "");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            e.SetResponse(503, "Server not configured");
            return;
        }

        var relative = e.Uri.AbsolutePath.Substring(MediaProxy.PathPrefix.Length) + e.Uri.Query;
        var target = new Uri(new Uri(serverUrl.EndsWith('/') ? serverUrl : serverUrl + "/"), relative);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, target);
            var apiKey = Preferences.Default.Get(MobileServerConfigService.ApiKeyKey, "");
            if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            var range = e.Headers?.FirstOrDefault(h => string.Equals(h.Key, "Range", StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrEmpty(range)) request.Headers.TryAddWithoutValidation("Range", range);

            // El estado y las cabeceras (206, Content-Range) deben conocerse antes de responder, así que se espera a las cabeceras.
            // Task.Run evita bloquear un posible contexto de sincronización; el cuerpo se transmite después en streaming.
            var response = Task.Run(() => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)).GetAwaiter().GetResult();

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Las respuestas de otro "origen" lógico necesitan CORS para que fetch() (epub.js) pueda leerlas; aquí es el mismo origen
                ["Access-Control-Allow-Origin"] = "*"
            };
            foreach (var name in ForwardedResponseHeaders)
            {
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                {
                    headers[name] = string.Join(", ", values);
                }
            }

            var body = Task.Run(() => response.Content.ReadAsStreamAsync()).GetAwaiter().GetResult();
            e.SetResponse((int)response.StatusCode, response.ReasonPhrase ?? "OK", headers, body);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaProxy] Error reenviando {target}: {ex.Message}");
            e.SetResponse(502, "Bad Gateway");
        }
    }
}
