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
        var range = e.Headers?.FirstOrDefault(h => string.Equals(h.Key, "Range", StringComparison.OrdinalIgnoreCase)).Value;

#if WINDOWS
        // En Windows el evento llega por el hilo de UI: se difiere la respuesta de WebView2 para no bloquearlo
        if (e.PlatformArgs?.RequestEventArgs is { } windowsArgs && e.PlatformArgs.Sender is { } coreWebView)
        {
            RespondDeferred(coreWebView, windowsArgs, target, range);
            return;
        }
#endif

        try
        {
            var request = BuildRequest(target, range);

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

    private static HttpRequestMessage BuildRequest(Uri target, string? range)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, target);
        var apiKey = Preferences.Default.Get(MobileServerConfigService.ApiKeyKey, "");
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        if (!string.IsNullOrEmpty(range)) request.Headers.TryAddWithoutValidation("Range", range);
        return request;
    }

#if WINDOWS
    private static void RespondDeferred(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView,
        Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs args, Uri target, string? range)
    {
        var deferral = args.GetDeferral();
        _ = Task.Run(async () =>
        {
            int status;
            string reason;
            string headers = "Access-Control-Allow-Origin: *";
            // WebView2 necesita un stream con posición: se carga la respuesta en memoria. El audio llega por
            // peticiones Range (trozos), así que no se descarga el archivo entero de una vez.
            var body = new MemoryStream();
            try
            {
                using var response = await _http.SendAsync(BuildRequest(target, range), HttpCompletionOption.ResponseHeadersRead);
                status = (int)response.StatusCode;
                reason = response.ReasonPhrase ?? "OK";
                foreach (var name in ForwardedResponseHeaders)
                {
                    if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                    {
                        headers += $"\r\n{name}: {string.Join(", ", values)}";
                    }
                }
                await response.Content.CopyToAsync(body);
                body.Position = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaProxy] Error reenviando {target}: {ex.Message}");
                status = 502;
                reason = "Bad Gateway";
            }

            // Los objetos de WebView2 solo se pueden usar desde su hilo de UI
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    args.Response = coreWebView.Environment.CreateWebResourceResponse(
                        System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(body), status, reason, headers);
                }
                finally
                {
                    deferral.Complete();
                }
            });
        });
    }
#endif
}
