# Descargar y restaurar copias de seguridad desde la app Android (ticket #24)

Fecha: 2026-11-04 · Alcance: QuimeraReader.Mobile (.NET MAUI 10, BlazorWebView) + UI compartida en QuimeraReader.Shared.

## Resumen y recomendación

1. **Causa**: en Android, los enlaces `<a href=… download target="_blank">` de *Ajustes → Sistema* apuntan (vía `BookService.GetMediaUrl`) a la ruta interceptada por `MediaProxyInterceptor`. El `WebView` de Android no guarda archivos por sí mismo: una descarga solo se "notifica a la aplicación anfitriona" mediante `DownloadListener.onDownloadStart` ([Android `DownloadListener`](https://developer.android.com/reference/android/webkit/DownloadListener)), y el host MAUI no registra ninguno. Además el atributo `target="_blank"` no abre nada dentro del BlazorWebView. Por eso el enlace no hace nada.
2. **Diseño recomendado**: una interfaz `IBackupTransferService` en `QuimeraReader.Shared` con implementación nativa en Mobile:
   - **Descarga**: `HttpClient` propio (timeout infinito, cabecera `X-Api-Key` de `IServerConfigService`, `HttpCompletionOption.ResponseHeadersRead`) → copia en streaming a un archivo temporal en `FileSystem.CacheDirectory` con progreso → **`CommunityToolkit.Maui` `FileSaver.SaveAsync`** para que el usuario elija dónde guardarlo (en Android usa `ACTION_CREATE_DOCUMENT` del Storage Access Framework, sin permisos) → borrar el temporal.
   - **Restauración**: `FilePicker.PickAsync` (MAUI Essentials) → `OpenReadAsync()` → `MultipartFormDataContent` con `StreamContent` y `HttpClient` con timeout infinito (lo mismo que ya hace `RestoreBackupAsync`, pero sin pasar por `InputFile`/JS interop).
   - **Web** (y de momento Desktop/Photino): se mantiene el enlace normal; la implementación por defecto devuelve `IsNative = false`.
3. **Alternativa secundaria**: hoja de compartir (`Share.RequestAsync(new ShareFileRequest…)`) como botón extra ("Enviar a Drive/Telegram…"), no como mecanismo principal.

## Situación actual en el código

- `QuimeraReader.API/Controllers/MaintenanceController.cs`
  - `GET api/maintenance/backup?format=json|zip`: el JSON se devuelve en memoria (KB); el ZIP se genera en un temporal `DeleteOnClose` y se devuelve con `File(stream, "application/zip", "quimera-copia-{stamp}.zip")`. Como es un `FileStream` buscable, ASP.NET Core envía `Content-Length` → el cliente puede calcular progreso real. **Ojo**: el servidor genera el ZIP completo *antes* de enviar el primer byte, así que con bibliotecas grandes hay una espera sin progreso (fase "Preparando copia…").
  - `POST api/maintenance/backup/restore`: `IFormFile file`, `RequestSizeLimit(long.MaxValue)` y `MultipartBodyLengthLimit = long.MaxValue`. Copia el upload a un temporal y restaura.
- `QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor` (líneas ~239-251): dos `<a … download target="_blank">` y un `<InputFile OnChange="RestoreBackupAsync" accept=".json,.zip">`.
- `SettingsPanel.razor.cs` → `RestoreBackupAsync`: `e.File.OpenReadStream(maxAllowedSize: 20 GB)`, `HttpClient` propio con `Timeout.InfiniteTimeSpan` y `X-Api-Key`. Esto ya resuelve el timeout de 25 s del cliente general (`MauiProgram.cs`).
- `QuimeraReader.Mobile/MauiProgram.cs`: ya hay patrón de servicios por plataforma (`INetworkStateService`, `ILocalMediaServer`, `IOfflineSyncWorker` en `QuimeraReader.Shared.Interfaces`, implementados en `QuimeraReader.Mobile.Services`). **No** se usa todavía `CommunityToolkit.Maui`.
- `AndroidManifest.xml`: solo `INTERNET` y `ACCESS_NETWORK_STATE`; `SupportedOSPlatformVersion` Android = 24.

## Opciones para guardar el archivo en Android

| Opción | Cómo funciona | Permisos | Pros | Contras |
|---|---|---|---|---|
| **CommunityToolkit.Maui `FileSaver`** (recomendada) | `Intent.ActionCreateDocument` + `ContentResolver.OpenFileDescriptor(uri, "wt")`, copia en bloques de 4 KB y reporta progreso ([código fuente Android](https://github.com/CommunityToolkit/Maui/blob/main/src/CommunityToolkit.Maui.Core/Essentials/FileSaver/FileSaverImplementation.android.cs)) | Ninguno en API ≥ 33/34; la doc pide `READ/WRITE_EXTERNAL_STORAGE` solo si el dispositivo es < 33 ([doc FileSaver](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/essentials/file-saver)) | Diálogo "Guardar como" del sistema; el usuario puede elegir Descargas, SD o Drive; multiplataforma (Windows/iOS/Mac también) | Añade dependencia (`CommunityToolkit.Maui`, última versión 15.0.1) y `UseMauiCommunityToolkit()` |
| SAF a mano (`ACTION_CREATE_DOCUMENT`) | Lo mismo que FileSaver pero escrito en `Platforms/Android` con `ActivityResult` | Ninguno: "no requiere permisos del sistema" ([Android SAF](https://developer.android.com/training/data-storage/shared/documents-files)) | Sin dependencias; se podría escribir directamente desde la red al `content://` sin temporal | Más código propio (resultado de actividad, MIME, errores) |
| `MediaStore.Downloads` | Insertar en la colección Descargas | Ninguno en Android 10+ ("can contribute to … `MediaStore.Downloads` without requesting any storage-related permissions", [Android 11 storage](https://developer.android.com/about/versions/11/privacy/storage)) | Sin diálogo, guarda directo en Descargas | El usuario no elige carpeta; solo API ≥ 29 (el mínimo de la app es 24); código específico |
| `WebView.setDownloadListener` + `DownloadManager` | El WebView avisa y `DownloadManager` descarga | — | Mantiene el `<a href>` | `DownloadManager` hace su propia petición: no pasa por el proxy interceptado (no hay URL http real con API key en cabecera), y no deja elegir destino. Descartada |
| Hoja de compartir (`ShareFileRequest`) | Copiar a caché y compartir con `FileProvider` ([doc Share](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share?view=net-maui-10.0)) | Ninguno | Útil para mandar la copia a Drive/Telegram/correo | No "guarda" en el teléfono; muchas apps receptoras no aceptan GB; recomendable restringir con `microsoft_maui_essentials_fileprovider_file_paths.xml` (sharing-root) |

### Restricciones de scoped storage (Android 10+)

- Apuntando a Android 11+ (net10.0-android apunta a un API reciente), `WRITE_EXTERNAL_STORAGE` "no longer provide[s] any additional access" y `requestLegacyExternalStorage` se ignora ([Android 11 storage](https://developer.android.com/about/versions/11/privacy/storage)). No hay que pedir permisos de almacenamiento: SAF y `MediaStore.Downloads` funcionan sin ellos.
- `ACTION_CREATE_DOCUMENT` no sobrescribe: si existe `quimera-copia-X.zip` el sistema crea `quimera-copia-X(1).zip` ([SAF](https://developer.android.com/training/data-storage/shared/documents-files)). El nombre ya lleva fecha y hora, así que no molesta.
- `ACTION_OPEN_DOCUMENT_TREE` (elegir carpeta) **no** permite elegir la carpeta `Download` desde Android 11; `ACTION_CREATE_DOCUMENT` (lo que usa FileSaver) sí permite guardar dentro de Descargas.
- Nada de `MANAGE_EXTERNAL_STORAGE` ("All files access"): innecesario y penalizado en Google Play.

## Restaurar desde el teléfono

- `FilePicker.Default.PickAsync(options)` usa `Intent.ActionOpenDocument` ([código MAUI](https://github.com/dotnet/maui/blob/main/src/Essentials/src/FilePicker/FilePicker.android.cs)); en Android filtrar por MIME: `application/zip`, `application/json` (y conviene añadir `application/octet-stream`, porque algunos gestores etiquetan mal los .zip). Hay que llamarlo en el hilo de UI ([doc FilePicker](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0)).
- Usar siempre `result.OpenReadAsync()`, nunca `File.OpenRead(result.FullPath)`: en Android `FullPath` puede ser `content://` (doc FilePicker).
- **Caveat importante para ZIP grandes**: en Android 10+ (API ≥ 29) MAUI no resuelve rutas físicas y **copia el contenido a la caché** de la app (`FileSystemUtils.EnsurePhysicalPath` → `CacheContentFile`, copia síncrona con `CopyTo`) ([código MAUI](https://github.com/dotnet/maui/blob/main/src/Essentials/src/FileSystem/FileSystemUtils.android.cs)). Con una copia de varios GB eso duplica el espacio ocupado y tarda antes de que `PickAsync` devuelva. Mitigaciones: mostrar "Preparando archivo…", borrar el `FullPath` de caché al terminar, y si se queda corto, implementar `ACTION_OPEN_DOCUMENT` a mano y leer con `ContentResolver.OpenInputStream(uri)` en streaming (sin copia).
- **Por qué no seguir con `InputFile`**: en un BlazorWebView los bytes cruzan del WebView a .NET por JS interop; la doc de Blazor advierte que el lado cliente usa un `ArrayBuffer` limitado a 2 GB / memoria del dispositivo y que "large file uploads may fail" ([Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0)). Con el picker nativo el stream va directo de disco a `HttpClient`.
- Subida: `MultipartFormDataContent` + `StreamContent` (con `Content-Length` si se conoce, para que el servidor no tenga que leer chunked) y `HttpClient { Timeout = InfiniteTimeSpan }`; pasar `CancellationToken` para un botón "Cancelar". Para progreso de subida, envolver el stream en un `ProgressStream` sencillo que cuente bytes leídos.

## Cómo llama la UI compartida al servicio de plataforma

Mismo patrón que `INetworkStateService` / `ILocalMediaServer`: interfaz en `QuimeraReader.Shared/Interfaces`, implementación por host, registrada en DI. Web registra una implementación "nula" (o se usa `TryAddScoped` en `ServiceCollectionExtensions`) y la UI decide:

```razor
@if (BackupTransfer.IsNative)
{
    <button class="btn btn-outline-light btn-sm" disabled="@_backupBusy" @onclick='() => DownloadBackupAsync("json")'>Estado</button>
    <button class="btn btn-outline-light btn-sm" disabled="@_backupBusy" @onclick='() => DownloadBackupAsync("zip")'>Completa</button>
    <button class="btn btn-outline-light btn-sm" disabled="@_isRestoring" @onclick="RestoreNativeAsync">Restaurar…</button>
}
else
{
    @* Web / Desktop: se mantienen los <a href … download> y el <InputFile> actuales *@
}
```

### Boceto de código

```csharp
// QuimeraReader.Shared/Interfaces/IBackupTransferService.cs
public interface IBackupTransferService
{
    /// <summary>true si descarga y restauración se hacen con APIs nativas (MAUI); false = enlace web normal.</summary>
    bool IsNative { get; }
    /// <returns>Ruta/nombre donde se guardó, o null si el usuario canceló.</returns>
    Task<string?> DownloadBackupAsync(string format, IProgress<double>? progress, CancellationToken ct);
    /// <returns>JSON del informe de restauración, o null si el usuario canceló.</returns>
    Task<JsonElement?> PickAndRestoreAsync(IProgress<double>? progress, CancellationToken ct);
}

public sealed class BrowserBackupTransferService : IBackupTransferService   // Web y Desktop
{
    public bool IsNative => false;
    public Task<string?> DownloadBackupAsync(string f, IProgress<double>? p, CancellationToken ct) => throw new NotSupportedException();
    public Task<JsonElement?> PickAndRestoreAsync(IProgress<double>? p, CancellationToken ct) => throw new NotSupportedException();
}
```

```csharp
// QuimeraReader.Mobile/Services/MauiBackupTransferService.cs
public sealed class MauiBackupTransferService(IServerConfigService config, IFileSaver fileSaver) : IBackupTransferService
{
    public bool IsNative => true;

    HttpClient CreateClient()
    {
        var c = new HttpClient { BaseAddress = new Uri(config.ServerUrl!.TrimEnd('/') + "/"), Timeout = Timeout.InfiniteTimeSpan };
        if (!string.IsNullOrEmpty(config.ApiKey)) c.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        return c;
    }

    public async Task<string?> DownloadBackupAsync(string format, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = CreateClient();
        using var resp = await http.GetAsync($"api/maintenance/backup?format={format}", HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var name = resp.Content.Headers.ContentDisposition?.FileNameStar
                   ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                   ?? $"quimera-copia.{format}";
        var total = resp.Content.Headers.ContentLength;

        var temp = Path.Combine(FileSystem.CacheDirectory, "backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(temp))
            {
                var buffer = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report(0.9 * done / total.Value);   // 90 % descarga, 10 % guardado
                }
            }
            await using var file = File.OpenRead(temp);                       // FileSaver necesita Length: stream buscable
            var result = await MainThread.InvokeOnMainThreadAsync(() =>
                fileSaver.SaveAsync(name, file, ct));                          // diálogo SAF "Guardar como"
            if (!result.IsSuccessful)
                return result.Exception is OperationCanceledException ? null : throw result.Exception!;
            progress?.Report(1);
            return result.FilePath;
        }
        finally { try { File.Delete(temp); } catch { } }
    }

    public async Task<JsonElement?> PickAndRestoreAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var types = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["application/zip", "application/json", "application/octet-stream"],
            [DevicePlatform.WinUI]   = [".zip", ".json"],
        });
        var picked = await MainThread.InvokeOnMainThreadAsync(() =>
            FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Elige una copia", FileTypes = types }));
        if (picked is null) return null;
        try
        {
            await using var stream = await picked.OpenReadAsync();
            using var content = new MultipartFormDataContent();
            var part = new StreamContent(stream, 81920);                      // envolver en un ProgressStream si se quiere progreso
            content.Add(part, "file", picked.FileName);                        // el servidor decide ZIP/JSON por la extensión
            using var http = CreateClient();
            using var resp = await http.PostAsync("api/maintenance/backup/restore", content, ct);
            return await resp.Content.ReadFromJsonAsync<JsonElement>(ct);     // el panel interpreta error / informe como hoy
        }
        finally
        {
            // En Android 10+ MAUI copió el archivo a la caché: liberarlo
            if (picked.FullPath.StartsWith(FileSystem.CacheDirectory, StringComparison.Ordinal))
                try { File.Delete(picked.FullPath); } catch { }
        }
    }
}
```

```csharp
// MauiProgram.cs
builder.UseMauiApp<App>().UseMauiCommunityToolkit();
builder.Services.AddSingleton<IFileSaver>(FileSaver.Default);
builder.Services.AddScoped<IBackupTransferService, MauiBackupTransferService>();
// Web/Program.cs y Desktop/Program.cs:
builder.Services.AddScoped<IBackupTransferService, BrowserBackupTransferService>();
```

Para `PickAndRestoreAsync`, la lógica que hoy interpreta el informe en `RestoreBackupAsync` (contadores `matchedByHash`, `imported`, …) puede extraerse a un método común del panel para que la usen tanto el `InputFile` web como el camino nativo.

## Caveats

- **Tamaño**: el ZIP completo puede ser de varios GB (EPUB + audios + portadas). Con el diseño propuesto ocupa temporalmente ~1× en caché (descarga) + 1× en el destino durante el guardado; en restauración, 1× extra en caché por la copia de MAUI. Comprobar espacio libre antes (`StatFs` / `DriveInfo` sobre `FileSystem.CacheDirectory`) y avisar si `Content-Length` > espacio disponible.
- **Alternativa sin temporal**: escribir directamente de la red al `content://` elegido (SAF a mano, o `FileSaver.SaveAsync` con un stream no buscable). Ojo: la implementación Android de FileSaver calcula el progreso con `stream.Length`, así que con un stream de red no buscable solo puede usarse la sobrecarga sin `IProgress` (con progreso, `Length` lanza `NotSupportedException`); por eso el boceto descarga primero a un temporal. Además, si se pide destino antes de descargar y la red falla, queda un archivo a medias en la carpeta del usuario.
- **Progreso de FileSaver**: reporta fracciones 0–1 durante la copia y al final `Report(100)` en el `finally` (inconsistencia del código fuente actual): normalizar en la UI (`Math.Min(1, v)`).
- **Espera inicial del ZIP**: el servidor construye el ZIP entero antes de responder; con muchas pistas de audio pueden ser minutos sin bytes. No usar timeouts cortos y mostrar "El servidor está preparando la copia…" hasta recibir cabeceras. Una mejora futura en el API sería generar la copia como trabajo en segundo plano (como `RunJobAsync`) y descargarla después por URL con `Range`/reanudación.
- **App en segundo plano**: si el usuario sale de la app durante una descarga larga, Android puede pausar/matar el proceso. Para copias muy grandes valdría la pena un *foreground service* o `WorkManager`; para una primera versión basta con avisar de mantener la app abierta (y `DeviceDisplay.KeepScreenOn = true` mientras dure).
- **http:// y API key**: el `HttpClient` nativo no sufre el bloqueo de *mixed content* del WebView (por eso existe `MediaProxyInterceptor`), pero el servidor en http:// necesita `usesCleartextTraffic`/network security config, que la app ya tiene que tener para el resto de llamadas.
- **Desktop (Photino)**: probablemente tampoco gestiona `download` en su WebView; la interfaz permite añadir una implementación con diálogo nativo más adelante.
- **iOS/Mac/Windows del proyecto MAUI**: `FileSaver` y `FilePicker` funcionan también (Mac Catalyst requiere entitlements `files.user-selected.read-write`), así que la misma implementación sirve en todos los TFM de `QuimeraReader.Mobile`.

## Fuentes

- CommunityToolkit.Maui FileSaver — https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/essentials/file-saver
- FileSaver, implementación Android — https://github.com/CommunityToolkit/Maui/blob/main/src/CommunityToolkit.Maui.Core/Essentials/FileSaver/FileSaverImplementation.android.cs
- .NET MAUI File picker — https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0
- FilePicker Android / FileSystemUtils (copia a caché) — https://github.com/dotnet/maui/blob/main/src/Essentials/src/FilePicker/FilePicker.android.cs · https://github.com/dotnet/maui/blob/main/src/Essentials/src/FileSystem/FileSystemUtils.android.cs
- .NET MAUI Share — https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share?view=net-maui-10.0
- Android Storage Access Framework — https://developer.android.com/training/data-storage/shared/documents-files
- Android 11 storage changes — https://developer.android.com/about/versions/11/privacy/storage
- Android `DownloadListener` — https://developer.android.com/reference/android/webkit/DownloadListener
- Blazor file uploads / downloads (.NET 10) — https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0 · https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads?view=aspnetcore-10.0
