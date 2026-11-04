using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Desktop.Services;

/// <summary>
/// Servidor HTTP de loopback que sirve al WebView de Photino las descargas offline (el WebView no carga rutas
/// del disco en &lt;audio src&gt;). Escucha solo en localhost, en un puerto libre, y exige un token aleatorio en la
/// ruta; solo sirve archivos sueltos de la carpeta de datos de la app. Atiende Range para poder saltar en el audio.
/// </summary>
public sealed class DesktopLocalMediaServer : ILocalMediaServer, IDisposable
{
    // Tramo máximo por respuesta: el reproductor pide el resto con nuevas peticiones Range
    private const long MaxChunk = 16 * 1024 * 1024;

    private readonly string _directory;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private readonly object _startLock = new();
    private HttpListener? _listener;
    private string? _baseUrl;

    public DesktopLocalMediaServer() : this(DesktopStorage.AppDataDirectory) { }

    public DesktopLocalMediaServer(string directory)
    {
        _directory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string? GetUrl(string localFilePath)
    {
        if (string.IsNullOrEmpty(localFilePath) || !File.Exists(localFilePath)) return null;
        var directory = Path.GetDirectoryName(Path.GetFullPath(localFilePath));
        if (!string.Equals(directory, _directory, StringComparison.OrdinalIgnoreCase)) return null;

        var baseUrl = EnsureStarted();
        return baseUrl == null ? null : baseUrl + Uri.EscapeDataString(Path.GetFileName(localFilePath));
    }

    private string? EnsureStarted()
    {
        lock (_startLock)
        {
            if (_baseUrl != null) return _baseUrl;
            try
            {
                var port = GetFreePort();
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
                _listener = listener;
                _baseUrl = $"http://localhost:{port}/{_token}/";
                _ = Task.Run(AcceptLoopAsync);
                return _baseUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalMediaServer] No se pudo arrancar: {ex.Message}");
                return null;
            }
        }
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener is { IsListening: true } listener)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                return; // listener detenido
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            var segments = context.Request.Url?.AbsolutePath.Trim('/').Split('/') ?? Array.Empty<string>();
            var path = segments.Length == 2 && segments[0] == _token ? LocalMediaFile.ResolveFile(_directory, segments[1]) : null;
            if (path == null)
            {
                response.StatusCode = 404;
                return;
            }

            var length = new FileInfo(path).Length;
            response.ContentType = LocalMediaFile.ContentType(path);
            response.AddHeader("Accept-Ranges", "bytes");

            var range = LocalMediaFile.ParseRange(context.Request.Headers["Range"], length, MaxChunk);
            if (range is { Satisfiable: false })
            {
                response.StatusCode = 416;
                response.AddHeader("Content-Range", range.ContentRange(length));
                return;
            }

            long start = range?.Start ?? 0;
            long count = range?.Length ?? length;
            if (range != null)
            {
                response.StatusCode = 206;
                response.AddHeader("Content-Range", range.ContentRange(length));
            }
            response.ContentLength64 = count;
            if (context.Request.HttpMethod == "HEAD") return;

            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[81920];
            while (count > 0)
            {
                var read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)));
                if (read == 0) break;
                await response.OutputStream.WriteAsync(buffer.AsMemory(0, read));
                count -= read;
            }
        }
        catch (HttpListenerException)
        {
            // El reproductor cancela peticiones al saltar en el audio: no es un error
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalMediaServer] Error sirviendo {context.Request.Url}: {ex.Message}");
            try { response.StatusCode = 500; } catch { }
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        try { _listener?.Close(); } catch { }
    }
}
