using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Infrastructure.Providers;
using QuimeraReader.Domain.Interfaces;

using QuimeraReader.API.BackgroundServices;

var builder = WebApplication.CreateBuilder(args);

// Configurar base de datos SQLite local
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=quimerareader.db"));

// Registrar HttpClient general
builder.Services.AddHttpClient();

// Inyectar Metadata Providers
builder.Services.AddScoped<IMetadataProvider, GoogleBooksMetadataProvider>();
builder.Services.AddScoped<IMetadataProvider, OpenLibraryMetadataProvider>();

// Registrar servicios de infraestructura
builder.Services.AddSingleton<AudioAlignmentQueue>();
builder.Services.AddScoped<EpubScannerService>();
builder.Services.AddScoped<AudioAlignmentService>();

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

app.UseAuthorization();
app.MapControllers();

app.Run();
