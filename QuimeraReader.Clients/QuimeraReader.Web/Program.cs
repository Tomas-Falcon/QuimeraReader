using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QuimeraReader.Shared.Extensions;
using QuimeraReader.Shared.Services;
using QuimeraReader.Web;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddRadzenComponents();
builder.Services.AddQuimeraServices();

// Apuntar SIEMPRE a la API (puerto 5166) en vez de al servidor de desarrollo de Blazor
var apiBaseUrl = builder.HostEnvironment.IsDevelopment() ? "http://localhost:5166/" : builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IScanService, ScanService>();
builder.Services.AddScoped<QuimeraReader.Shared.Intetfaces.ITranslationService, TranslationService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<IServerConfigService, QuimeraReader.Web.Services.WebServerConfigService>();

await builder.Build().RunAsync();
