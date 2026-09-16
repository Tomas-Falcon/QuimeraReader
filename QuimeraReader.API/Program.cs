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

var builder = WebApplication.CreateBuilder(args);

var dbPath = Environment.GetEnvironmentVariable("QUIMERA_DB_PATH") ?? "quimerareader.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Registrar HttpClient general
builder.Services.AddHttpClient();

// Inyectar Metadata Providers
builder.Services.AddScoped<IMetadataProvider, GoogleBooksMetadataProvider>();
builder.Services.AddScoped<IMetadataProvider, OpenLibraryMetadataProvider>();

// Registrar servicios de infraestructura
builder.Services.AddSingleton<AudioAlignmentQueue>();
builder.Services.AddScoped<EpubScannerService>();
builder.Services.AddScoped<AudioAlignmentService>();
builder.Services.AddScoped<MediaPackagerService>();

// Registrar el Background Service
builder.Services.AddHostedService<LibraryScanBackgroundService>();
builder.Services.AddHostedService<AudioAlignmentBackgroundService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Asegurar que la BD se crea
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
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
