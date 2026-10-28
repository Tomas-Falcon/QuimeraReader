using Polly;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Infrastructure.Providers;
using QuimeraReader.Domain.Interfaces;
using QuimeraReader.API.BackgroundServices;
using QuimeraReader.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configurar logging: JSON para producción (Docker), texto simple para desarrollo
if (builder.Environment.IsProduction())
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
}

var dbPath = Environment.GetEnvironmentVariable("QUIMERA_DB_PATH") ?? "quimerareader.db";
// Nota: sin Cache=Shared (en modo WAL provoca bloqueos SQLITE_LOCKED entre lectores y escritores)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Default Timeout=30;"));

// Compresión Brotli/Gzip para los ~25 MB de .wasm/.js/.css de Blazor y las respuestas JSON
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/wasm", "application/octet-stream", "application/javascript", "text/javascript", "image/svg+xml"
    });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

// Enlazar la interfaz de Aplicación con la implementación de Infraestructura
builder.Services.AddScoped<QuimeraReader.Application.Interfaces.IAppDbContext>(provider => 
    provider.GetRequiredService<AppDbContext>());

// Registrar HttpClient general
builder.Services.AddHttpClient();

// Inyectar Metadata Providers
builder.Services.AddHttpClient<IMetadataProvider, GoogleBooksMetadataProvider>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(600)));
builder.Services.AddHttpClient<IMetadataProvider, OpenLibraryMetadataProvider>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(600)));
builder.Services.AddHttpClient<IMetadataProvider, HardcoverMetadataProvider>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(600)));
builder.Services.AddScoped<IMetadataProvider, GoodreadsScraperProvider>();
builder.Services.AddScoped<IMetadataProvider, StoryGraphScraperProvider>();

// Registrar servicios de infraestructura
builder.Services.AddSingleton<LibraryScanState>();
builder.Services.AddSingleton<AudioAlignmentQueue>();
builder.Services.AddScoped<EpubScannerService>();
builder.Services.AddScoped<QuimeraReader.Application.Interfaces.IEpubScannerService>(sp => sp.GetRequiredService<EpubScannerService>());
builder.Services.AddScoped<AudioAlignmentService>();
builder.Services.AddScoped<AudioMatchingService>();
builder.Services.AddScoped<QuimeraReader.Application.Interfaces.IAudioMatchingService>(sp => sp.GetRequiredService<AudioMatchingService>());
builder.Services.AddScoped<DeepLibraryScannerService>();
builder.Services.AddScoped<MediaPackagerService>();
builder.Services.AddScoped<QuimeraReader.Infrastructure.Services.HardcoverSyncService>();

// Registrar los Background Services
builder.Services.AddHostedService<LibraryScanBackgroundService>();
builder.Services.AddHostedService<AudioAlignmentBackgroundService>();
builder.Services.AddHostedService<MaintenanceBackgroundService>();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(QuimeraReader.Application.Interfaces.IAppDbContext).Assembly));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

// Middleware de logging y manejo de errores
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Asegurar que la BD se migra correctamente en cada arranque y habilitar WAL para concurrencia lectora/escritora
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
    try
    {
        dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;");
    }
    catch { }

    // Índices que faltaban sobre Books (138k filas): estados de lectura, offline, título (escáner), etc.
    string[] indexSql =
    {
        "CREATE INDEX IF NOT EXISTS IX_Books_ReadingStatus_LastReadAt ON Books (ReadingStatus, LastReadAt);",
        "CREATE INDEX IF NOT EXISTS IX_Books_LastReadAt ON Books (LastReadAt);",
        "CREATE INDEX IF NOT EXISTS IX_Books_IsAvailableOffline ON Books (IsAvailableOffline);",
        "CREATE INDEX IF NOT EXISTS IX_Books_Title ON Books (Title);",
        "CREATE INDEX IF NOT EXISTS IX_Books_SourceFilePath ON Books (SourceFilePath);",
        "CREATE INDEX IF NOT EXISTS IX_Books_ProcessingStatus ON Books (ProcessingStatus);",
        "CREATE INDEX IF NOT EXISTS IX_Books_AverageRating ON Books (AverageRating);",
        "CREATE INDEX IF NOT EXISTS IX_BookAuthors_AuthorId ON BookAuthors (AuthorId);",
        "CREATE INDEX IF NOT EXISTS IX_BookCategories_CategoryId ON BookCategories (CategoryId);",
        "CREATE INDEX IF NOT EXISTS IX_BookAudioTracks_BookId ON BookAudioTracks (BookId);"
    };
    foreach (var sql in indexSql)
    {
        try { dbContext.Database.ExecuteSqlRaw(sql); } catch (Exception ex) { Console.WriteLine($"[Startup] No se pudo crear índice: {ex.Message}"); }
    }
    try { dbContext.Database.ExecuteSqlRaw("PRAGMA optimize;"); } catch { }
}

app.UseResponseCompression();

// Configurar los Content Types para que la API pueda servir los archivos de Blazor WebAssembly sin dar Error 404
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".wasm"] = "application/wasm";
provider.Mappings[".dll"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    OnPrepareResponse = ctx =>
    {
        // index.html nunca debe cachearse (evita importmaps obsoletos tras un redeploy)
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        }
    }
}); // Servir wwwroot (Blazor WebAssembly)

app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/api/health");

app.MapFallbackToFile("index.html"); // SPA Fallback para enrutamiento Blazor

app.Run();
