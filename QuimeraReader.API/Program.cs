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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Enlazar la interfaz de Aplicación con la implementación de Infraestructura
builder.Services.AddScoped<QuimeraReader.Application.Interfaces.IAppDbContext>(provider => 
    provider.GetRequiredService<AppDbContext>());

// Registrar HttpClient general
builder.Services.AddHttpClient();

// Inyectar Metadata Providers
builder.Services.AddScoped<IMetadataProvider, GoogleBooksMetadataProvider>();
builder.Services.AddScoped<IMetadataProvider, OpenLibraryMetadataProvider>();
builder.Services.AddScoped<IMetadataProvider, HardcoverMetadataProvider>();

// Registrar servicios de infraestructura
builder.Services.AddSingleton<LibraryScanState>();
builder.Services.AddSingleton<AudioAlignmentQueue>();
builder.Services.AddScoped<EpubScannerService>();
builder.Services.AddScoped<AudioAlignmentService>();
builder.Services.AddScoped<MediaPackagerService>();
builder.Services.AddScoped<QuimeraReader.Infrastructure.Services.HardcoverSyncService>();

// Registrar el Background Service
builder.Services.AddHostedService<LibraryScanBackgroundService>();
builder.Services.AddHostedService<AudioAlignmentBackgroundService>();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(QuimeraReader.Application.Interfaces.IAppDbContext).Assembly));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
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

// Asegurar que la BD se migra correctamente en cada arranque
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

// Configurar los Content Types para que la API pueda servir los archivos de Blazor WebAssembly sin dar Error 404
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".wasm"] = "application/wasm";
provider.Mappings[".dll"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
}); // Servir wwwroot (Blazor WebAssembly)

app.UseAuthorization();
app.MapControllers();

app.MapFallbackToFile("index.html"); // SPA Fallback para enrutamiento Blazor

app.Run();
