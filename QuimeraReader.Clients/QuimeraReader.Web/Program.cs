using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QuimeraReader.Web;
using QuimeraReader.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IQuimeraApiClient, QuimeraApiClient>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<IServerConfigService, QuimeraReader.Web.Services.WebServerConfigService>();

await builder.Build().RunAsync();
