# Whisper por API externa (OpenAI, Ollama, LM Studio, servidores locales)

Ticket: #27. Estado: decidido implementar. Fecha de la investigación: 2026-11-04.

## TL;DR

- El pipeline solo usa **texto y start/end por segmento** de Whisper.net. No usa timestamps por palabra. Con `response_format=verbose_json` y `timestamp_granularities[]=segment` basta.
- **OpenAI en la nube:** el único modelo que documenta timestamps es `whisper-1`, y está **deprecado: se apaga el 26 de febrero de 2027**. Su reemplazo `gpt-transcribe` no documenta timestamps. Si OpenAI no añade timestamps a `gpt-transcribe`, la opción "OpenAI" deja de servir para el karaoke desde esa fecha.
- **Self-hosted compatibles:** whisper.cpp server, Speaches y LocalAI devuelven `verbose_json` con segmentos. Ollama tiene `/v1/audio/transcriptions` y `verbose_json` (un segmento por fragmento de audio), pero no `timestamp_granularities`. **LM Studio no ofrece speech-to-text.**
- **Diseño:** una interfaz `ITranscriptionProvider` con dos implementaciones (`LocalWhisperNetProvider`, `OpenAiCompatibleProvider`). FFmpeg corta el audio en trozos WAV de 16 kHz mono de unos 10 min (≈19 MB, por debajo de 25 MB y aceptado por todos los servidores). Cada trozo suma su offset a los tiempos. Polly reintenta 5xx, 408 y 429. El error se guarda en el libro y se muestra en la UI.

## 1. Qué consume hoy el pipeline de Whisper.net

Código revisado en `origin/dev` (22c9b4a):

| Lugar | Qué hace con Whisper.net | Datos que usa |
|---|---|---|
| `QuimeraReader.Infrastructure/Services/AudioAlignmentService.cs` L53-134 (`GenerateMultiTrackSyncMapAsync`) | Convierte cada pista a WAV 16 kHz mono PCM s16le con FFmpeg (L70) y llama a `processor.ProcessAsync` con `WithLanguage("auto")` | `result.Text`, `result.Start.TotalSeconds` y `result.End.TotalSeconds` por **segmento** (L107-116) |
| mismo archivo, L308-327 (`SyncSegment`) | Serializa el SyncMap como JSON | `trackNumber`, `spineIndex`, `chapterHref`, `text`, `start`, `end` (segundos, relativos a **la pista**) |
| mismo archivo, `MatchSegmentToChapter` / `ComputeTrigramCoverage` | Ancla cada segmento a un capítulo por trigramas y calcula la cobertura | solo el texto |
| `QuimeraReader.Infrastructure/Services/AudioMatchingService.cs` L68-110 (`MatchByContentAsync`) | Transcribe los primeros 900 s (`-t 900`) de un audio huérfano para emparejarlo con un libro | solo el texto concatenado; no usa tiempos |
| `QuimeraReader.API/BackgroundServices/AudioAlignmentBackgroundService.cs` L139-147 | Si `Success == false`, registra el error en el log y pone `ProcessingStatus = "ERROR"` | `ErrorMessage` **no se persiste**: la UI solo ve "ERROR" |

Settings existentes: `WhisperModelPath`, `WhisperMode` (`LocalServer` / `ExternalApi`), `OpenAIEndpoint` (por defecto `https://api.openai.com/v1/`) y `OpenAIApiKey`. `OpenAIApiKey` ya está en `SecretKeys` de `SettingsController` (L27). Ver `SettingsPanel.razor` L127-160 y `SettingsPanel.razor.cs` L41-76 y L226-232. **Nadie lee `WhisperMode` en el servidor.** Falta un setting para el **nombre del modelo** remoto.

Consecuencias:
- Los segmentos de Whisper (frases de unos segundos) bastan. Los timestamps por palabra no hacen falta para alcanzar lo que hay hoy. El karaoke resalta frases (`highlightKaraokePhrase` en `epubInterop.js`).
- Los tiempos son relativos a cada pista. Si una pista se trocea, hay que sumar el offset del trozo **dentro de la pista**, no el de todo el libro.

## 2. OpenAI `POST /v1/audio/transcriptions`

Fuentes: guía [Speech to text](https://developers.openai.com/api/docs/guides/speech-to-text), [referencia de la API](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create), [Deprecations](https://developers.openai.com/api/docs/deprecations), [Pricing](https://developers.openai.com/api/docs/pricing) y la spec [openai-openapi](https://github.com/openai/openai-openapi) (`CreateTranscriptionRequest`, `AudioResponseFormat`, `TranscriptionSegment`).

| Modelo | Formatos de respuesta | Timestamps | Precio | Estado |
|---|---|---|---|---|
| `whisper-1` | `json`, `text`, `srt`, `vtt`, `verbose_json` | Sí: `timestamp_granularities[]` = `segment` y/o `word`; según la guía, solo con `whisper-1` | $0.006/min | **Deprecado. Apagado el 26 feb 2027** |
| `gpt-4o-transcribe` / `gpt-4o-mini-transcribe` | "the only supported format is `json`" | No | ≈$0.006 / ≈$0.003 por min (cobro por tokens) | Deprecados. Apagado el 26 feb 2027 |
| `gpt-4o-transcribe-diarize` | `json`, `text`, `diarized_json` | Por segmento, con `speaker`/`start`/`end` en `diarized_json`. No admite `timestamp_granularities` | — | Deprecado. Apagado el 26 feb 2027 |
| `gpt-transcribe` (reemplazo recomendado) | La guía lo describe como JSON con `text` y `languages` | **No documentados** | $0.0045/min | Actual |

Más detalles:
- **Tamaño:** "Files can be up to 25 MB" (guía). La referencia de la API no da un máximo.
- **Formatos:** flac, mp3, mp4, mpeg, mpga, m4a, ogg, wav y webm (referencia de la API).
- **`verbose_json`:** `segments[]` con `id`, `seek`, `start`, `end`, `text`, `tokens`, `temperature`, `avg_logprob`, `compression_ratio` y `no_speech_prob`, en segundos. Con `word`, también `words[]` (`word`, `start`, `end`). Los timestamps por palabra añaden latencia; los de segmento no (spec).
- **Audio largo:** la guía pide comprimir o partir en trozos de 25 MB o menos, evitar cortar a mitad de frase y pasar contexto del trozo anterior en `prompt`. Para `whisper-1` el `prompt` admite 224 tokens.
- **`language`:** acepta ISO-639-1. Indicarlo mejora la precisión y la latencia (spec). Conviene pasar el idioma del libro si se conoce.

**Implicación clave:** hoy el modo "OpenAI" solo puede ser `whisper-1`, y tiene fecha de caducidad. El proveedor debe **validar que la respuesta trae `segments` con tiempos**. Si no los trae, debe fallar con un mensaje claro ("el modelo X no devuelve timestamps; el karaoke los necesita"), no generar un SyncMap vacío. Antes de febrero de 2027 hay que revisar si `gpt-transcribe` ya admite `verbose_json`.

## 3. Servidores compatibles self-hosted

| Servidor | Ruta OpenAI | `verbose_json` con segmentos | Palabras | Notas | Fuente |
|---|---|---|---|---|---|
| **whisper.cpp server** | Por defecto `POST /inference`. Se puede exponer como OpenAI con `--request-path /v1/audio --inference-path /transcriptions` | Sí: `segments[]` con `id`, `text` y `start`/`end` en segundos (`t0 * 0.01`) | Timestamps por token (activados con `verbose_json`) | Espera WAV. `--convert` usa FFmpeg en el servidor. El campo `model` se ignora: el modelo se carga al arrancar o con `/load` | [server.cpp](https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/server.cpp) (L40, L574-635, L1065+), [README](https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/README.md) |
| **Speaches** (antes faster-whisper-server) | `POST /v1/audio/transcriptions` | Sí: `text`, `json`, `verbose_json`, `srt`, `vtt` | `timestamp_granularities[]` (word/segment), validado igual que OpenAI | Modelos faster-whisper (p. ej. `Systran/faster-whisper-small`) | [routers/stt.py](https://github.com/speaches-ai/speaches/blob/master/src/speaches/routers/stt.py), [docs](https://speaches.ai/usage/speech-to-text/) |
| **LocalAI** | `POST /v1/audio/transcriptions` | Sí: segmentos y palabras en segundos; con `json` los elimina | `timestamp_granularities` (acepta `[]` o sin corchetes) | Backend whisper u otros | [transcription.go](https://github.com/mudler/LocalAI/blob/master/core/http/endpoints/openai/transcription.go) |
| **Ollama** | `POST /v1/audio/transcriptions` (registrado en `server/routes.go`) | Sí, "one segment per transcribed piece of audio". Ollama corta en pausas el audio de más de 30 s | **No** (`timestamp_granularities` sin soporte). No hay `srt`/`vtt` | **Solo WAV**, hasta 512 MB y 2 horas. Necesita un modelo con capacidad de audio. Los segmentos salen de su troceo por pausas (≤30 s): más gruesos que los de Whisper, pero válidos para frases | [openai-compatibility](https://github.com/ollama/ollama/blob/main/docs/api/openai-compatibility.mdx), [routes.go](https://github.com/ollama/ollama/blob/main/server/routes.go) |
| **LM Studio** | **No existe.** Sus endpoints compatibles son `/v1/models`, `/v1/responses`, `/v1/decisions`, `/v1/chat/completions`, `/v1/embeddings` y `/v1/completions` | — | — | No hace speech-to-text. Hay que corregir el texto de la UI ("Ollama / LLMStudio") | [LM Studio OpenAI compat](https://lmstudio.ai/docs/developer/openai-compat) |

Pendiente de verificar con una llamada real: la forma exacta de los segmentos de Ollama (que `start` y `end` vengan rellenos) y qué modelos de su biblioteca aceptan audio. La documentación no da ejemplos de respuesta.

## 4. Trocear audiolibros largos con FFmpeg

Restricciones que marcan el tamaño del trozo:
- OpenAI: 25 MB por archivo.
- Ollama: solo WAV.
- whisper.cpp sin `--convert`: solo WAV.
- WAV 16 kHz, mono, s16le = 32 000 B/s ≈ **1,92 MB/min**. Un trozo de **10 min ≈ 19,2 MB** cabe en el límite de OpenAI y lo aceptan todos los servidores. Así no hace falta un códec distinto para cada proveedor.

Opciones de FFmpeg ([ffmpeg-formats: segment](https://ffmpeg.org/ffmpeg-formats.html#segment), [ffmpeg-filters: silencedetect](https://ffmpeg.org/ffmpeg-filters.html#silencedetect), [ffmpeg: -ss](https://ffmpeg.org/ffmpeg.html)):
- `-f segment -segment_time 600` corta cada 600 s. `-segment_times t1,t2,...` corta en puntos concretos.
- `-segment_list chunks.csv` escribe `segment_filename,segment_start_time,segment_end_time` por trozo. **Esos son los offsets reales** que hay que sumar.
- `-reset_timestamps 1` hace que cada trozo empiece en t≈0.
- `silencedetect=noise=-35dB:d=0.4` registra en stderr `silence_start` / `silence_end`. Sirve para elegir, cerca de cada múltiplo de 600 s, un punto de corte en silencio y no a mitad de palabra, como pide la guía de OpenAI.
- PCM no tiene keyframes, así que en WAV el corte es exacto a la muestra.

Pipeline propuesto por pista:
1. Si el proveedor es remoto y el archivo original cabe tal cual (≤ 24 MB y en un formato aceptado), enviarlo sin trocear. Ojo: Ollama solo acepta WAV.
2. Si no cabe: (a) una pasada `ffmpeg -i in -af silencedetect=noise=-35dB:d=0.4 -f null -` para obtener los silencios; (b) elegir puntos de corte cerca de 600 s, 1200 s... (como mucho 660 s por trozo para no pasar de 25 MB); (c) `ffmpeg -nostdin -i in -ar 16000 -ac 1 -c:a pcm_s16le -f segment -segment_times <cortes> -reset_timestamps 1 -segment_list chunks.csv chunk_%04d.wav`.
3. Transcribir los trozos en orden. Pasar como `prompt` los últimos ~200 caracteres del trozo anterior, por continuidad.
4. Unir: `seg.Start = chunkStart + s.start`, `seg.End = chunkStart + s.end`. Descartar segmentos vacíos y solapes (si `s.start + chunkStart < últimoEnd`, recortar el inicio).

Para `AudioMatchingService` (900 s ≈ 28,8 MB en WAV), basta con mandar **2 trozos** o reducir a `-t 600`: para emparejar por trigramas sobra.

## 5. Diseño recomendado

### Abstracción

```csharp
// QuimeraReader.Application/Interfaces
public sealed record TranscriptSegment(double Start, double End, string Text);

public interface ITranscriptionProvider
{
    /// Transcribe un WAV 16 kHz mono. Los tiempos son relativos al archivo recibido.
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        string wavPath, TranscriptionOptions options, CancellationToken ct);
    bool RequiresChunking { get; }   // false para Whisper.net local
}

public sealed record TranscriptionOptions(string? Language, string? Prompt);
```

- `LocalWhisperNetProvider`: el código actual (L99-116) movido aquí. Conserva el `WhisperFactory` (cacheado por ruta de modelo) y no trocea.
- `OpenAiCompatibleProvider`: un typed `HttpClient` contra `{OpenAIEndpoint}/audio/transcriptions`, con multipart `file`, `model`, `response_format=verbose_json`, `timestamp_granularities[]=segment` (solo si el modelo no es de Ollama; ver nota), `language` y `prompt`.
- `TranscriptionProviderFactory`: lee `WhisperMode` de `SystemSettings` y devuelve el proveedor. Lo usan `AudioAlignmentService` y `AudioMatchingService`. Así desaparece la lógica de Whisper.net duplicada en los dos servicios.
- `AudioChunker` (FFmpeg): `Task<IReadOnlyList<(string Path, double Offset)>> SplitAsync(string input, TimeSpan target, CancellationToken)`. Lo usa `AudioAlignmentService` cuando `RequiresChunking`.

### Sketch del proveedor HTTP

```csharp
public sealed class OpenAiCompatibleProvider(HttpClient http, IOptionsSnapshot<ExternalWhisperOptions> opt)
    : ITranscriptionProvider
{
    public bool RequiresChunking => true;

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        string wavPath, TranscriptionOptions o, [EnumeratorCancellation] CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(File.OpenRead(wavPath));
        file.Headers.ContentType = new("audio/wav");
        form.Add(file, "file", Path.GetFileName(wavPath));
        form.Add(new StringContent(opt.Value.Model), "model");             // p. ej. "whisper-1"
        form.Add(new StringContent("verbose_json"), "response_format");
        if (opt.Value.SendTimestampGranularities)                           // false para Ollama
            form.Add(new StringContent("segment"), "timestamp_granularities[]");
        if (!string.IsNullOrEmpty(o.Language)) form.Add(new StringContent(o.Language), "language");
        if (!string.IsNullOrEmpty(o.Prompt))   form.Add(new StringContent(o.Prompt), "prompt");

        using var resp = await http.PostAsync("audio/transcriptions", form, ct);
        if (!resp.IsSuccessStatusCode)
            throw new TranscriptionException((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));

        var body = await resp.Content.ReadFromJsonAsync<VerboseJson>(cancellationToken: ct)
                   ?? throw new TranscriptionException(0, "Respuesta vacía");
        if (body.Segments is not { Count: > 0 })
            throw new TranscriptionException(0,
                $"El modelo '{opt.Value.Model}' no devolvió segmentos con tiempos (verbose_json). El karaoke los necesita.");

        foreach (var s in body.Segments)
            if (!string.IsNullOrWhiteSpace(s.Text))
                yield return new TranscriptSegment(s.Start, s.End, s.Text.Trim());
    }

    private sealed record VerboseJson(string? Text, List<Seg>? Segments);
    private sealed record Seg(double Start, double End, string Text);
}
```

Uso en `AudioAlignmentService`, por pista:

```csharp
var chunks = provider.RequiresChunking
    ? await chunker.SplitAsync(audioFilePath, TimeSpan.FromMinutes(10), ct)
    : [(tempWavFile, 0d)];
string? prompt = null;
foreach (var (path, offset) in chunks)
{
    await foreach (var s in provider.TranscribeAsync(path, new(language, prompt), ct))
    {
        var seg = new SyncSegment { TrackNumber = trackNumber, Text = s.Text,
                                    Start = offset + s.Start, End = offset + s.End };
        // ...MatchSegmentToChapter igual que hoy...
        allSegments.Add(seg);
        prompt = Tail(s.Text, 200);
    }
}
```

### Registro, Polly y timeouts

El proyecto ya usa `Microsoft.Extensions.Http.Polly` (`Program.cs` L54-59). `AddTransientHttpErrorPolicy` cubre `HttpRequestException`, 5xx y 408, pero **no 429** ([docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.pollyhttpclientbuilderextensions.addtransienthttperrorpolicy)). OpenAI devuelve 429 por rate limit, así que hay que añadirlo:

```csharp
builder.Services.AddHttpClient<OpenAiCompatibleProvider>(c => c.Timeout = TimeSpan.FromMinutes(15))
    .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
        .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(4, n => TimeSpan.FromSeconds(Math.Pow(2, n)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500))));
```

- **Timeout:** el de `HttpClient` es de 100 s por defecto ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout)). Un servidor local en CPU puede tardar varios minutos con 10 min de audio. Conviene 10-15 min.
- **Reintentos:** reintentar solo 5xx, 408, 429 y errores de red. 400, 401 y 413 son errores de configuración (modelo inválido, clave mala, trozo demasiado grande): fallar enseguida con un mensaje legible.
- **Contenido multipart:** reintentar el mismo `HttpContent` con un `StreamContent` ya leído falla. O se reconstruye el `MultipartFormDataContent` dentro de un reintento propio en el proveedor, o se usa `ByteArrayContent` (un trozo de 19 MB en memoria es asumible). Lo más simple es poner el reintento con Polly **en el proveedor** (`ResiliencePipeline` que envuelve la construcción del form) y no en el handler.
- **Base URL:** normalizar `OpenAIEndpoint` con `/` final. Ejemplos para la UI: `https://api.openai.com/v1/`, `http://host:11434/v1/` (Ollama), `http://host:8000/v1/` (Speaches), `http://host:8080/v1/` (LocalAI) y, para whisper.cpp, arrancarlo con `--request-path /v1/audio --inference-path /transcriptions`.
- **Clave API opcional:** los servidores locales no la piden. Enviar `Authorization: Bearer` solo si `OpenAIApiKey` no está vacía.

### Settings y UI

- Nuevos settings: `WhisperApiModel` (por defecto `whisper-1`) y, opcional, `WhisperApiLanguage`. `SendTimestampGranularities` puede inferirse: si una respuesta 400 menciona `timestamp_granularities`, reintentar sin él. También puede ser una casilla avanzada.
- Botón **"Probar conexión"** en `SettingsPanel`: un endpoint `POST /api/settings/whisper/test` que manda ~5 s de silencio o un WAV de muestra y comprueba que llegan `segments` con `start`/`end`. Así el problema del modelo sin timestamps sale en Settings y no a mitad de un audiolibro.
- Cambiar el texto del desplegable: quitar "LLMStudio" y escribir "OpenAI (whisper-1), Ollama, Speaches, LocalAI, whisper.cpp".
- **Errores visibles:** hoy `ProcessingStatus = "ERROR"` sin motivo. Añadir `Book.ProcessingError` (string nullable), guardarlo en `AudioAlignmentBackgroundService` (L141-146) y mostrarlo en el detalle del libro o en un tooltip. Requiere una migración. Recordar el commit 3eaf5f8: el esquema de la base de datos local de los clientes debe seguir sincronizado con el modelo. Alternativa sin migración: guardar el último error en `SystemSettings` con clave `AlignmentError:{bookId}`.
- **Progreso:** con trozos se puede informar "trozo 3/18 de la pista 2". Hoy no hay progreso.

## 6. Caveats

1. **Fecha límite de OpenAI:** `whisper-1` se apaga el 26 feb 2027 y `gpt-transcribe` no documenta timestamps. Revisar antes de esa fecha. Si no hay alternativa con timestamps, el modo OpenAI en la nube solo servirá para emparejar audios huérfanos (texto), no para el karaoke. Para el karaoke, documentar Speaches o whisper.cpp self-hosted como opción recomendada.
2. **Granularidad distinta:** Ollama corta en pausas (≤30 s por segmento) y Whisper.net en frases cortas. Un segmento largo hace que el karaoke resalte un bloque grande. Funciona, pero es menos fino.
3. **Coste y privacidad:** un audiolibro de 10 h con `whisper-1` cuesta 600 min × $0.006 = **$3.60**. El audio sale del servidor: avisarlo en la UI.
4. **Cortes:** cortar a mitad de palabra rompe un segmento y empeora los trigramas. Por eso se corta en silencios y se pasa `prompt`.
5. **Disco temporal:** trocear una pista de varias horas a WAV ocupa ~115 MB/h. Borrar cada trozo al terminarlo, como ya se hace con `tempWavFile`.
6. **Idioma:** hoy se usa `auto`. En remoto, pasar `language` (si el EPUB lo trae en metadatos) mejora la precisión y la latencia.
7. **`SyncSegment` no cambia:** los tiempos siguen siendo relativos a la pista. Los clientes no necesitan cambios.

## Fuentes

- OpenAI: [guía Speech to text](https://developers.openai.com/api/docs/guides/speech-to-text) · [referencia createTranscription](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create) · [Deprecations (2026-08-26: Transcription models)](https://developers.openai.com/api/docs/deprecations) · [Pricing](https://developers.openai.com/api/docs/pricing) · [openai-openapi spec](https://github.com/openai/openai-openapi)
- whisper.cpp: [examples/server/server.cpp](https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/server.cpp) · [README](https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/README.md)
- Speaches: [src/speaches/routers/stt.py](https://github.com/speaches-ai/speaches/blob/master/src/speaches/routers/stt.py) · [docs STT](https://speaches.ai/usage/speech-to-text/)
- LocalAI: [core/http/endpoints/openai/transcription.go](https://github.com/mudler/LocalAI/blob/master/core/http/endpoints/openai/transcription.go)
- Ollama: [docs/api/openai-compatibility.mdx](https://github.com/ollama/ollama/blob/main/docs/api/openai-compatibility.mdx) · [server/routes.go](https://github.com/ollama/ollama/blob/main/server/routes.go)
- LM Studio: [OpenAI compatibility](https://lmstudio.ai/docs/developer/openai-compat)
- FFmpeg: [formats (segment)](https://ffmpeg.org/ffmpeg-formats.html#segment) · [filters (silencedetect)](https://ffmpeg.org/ffmpeg-filters.html#silencedetect) · [ffmpeg (-ss)](https://ffmpeg.org/ffmpeg.html)
- .NET: [AddTransientHttpErrorPolicy](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.pollyhttpclientbuilderextensions.addtransienthttperrorpolicy) · [HttpClient.Timeout](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout)
