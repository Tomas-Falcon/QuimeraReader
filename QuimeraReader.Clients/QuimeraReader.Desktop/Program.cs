using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Blazor;
using Radzen;
using QuimeraReader.Desktop.Data;
using QuimeraReader.Desktop.Services;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // UI Components
        appBuilder.Services.AddRadzenComponents();
        appBuilder.Services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        // Desktop Services
        appBuilder.Services.AddSingleton<INetworkStateService, DesktopNetworkStateService>();
        appBuilder.Services.AddScoped<IServerConfigService, DesktopServerConfigService>();

        appBuilder.Services.AddScoped(sp =>
        {
            var config = sp.GetRequiredService<IServerConfigService>();
            var defaultUrl = "http://localhost:5000/";
            var url = config.ServerUrl ?? defaultUrl;
            return new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
        });

        // Core Shared Services
        appBuilder.Services.AddScoped<IBookService, BookService>();
        appBuilder.Services.AddScoped<ISettingsService, SettingsService>();
        appBuilder.Services.AddScoped<IScanService, ScanService>();
        appBuilder.Services.AddScoped<ITranslationService, TranslationService>();
        appBuilder.Services.AddScoped<ToastService>();

        // Local SQLite Database and Offline Sync
        appBuilder.Services.AddDbContext<DesktopLocalAppDbContext>();
        appBuilder.Services.AddScoped<ILocalBookRepository, DesktopLocalBookRepository>();
        appBuilder.Services.AddSingleton<IOfflineSyncWorker, DesktopOfflineSyncWorker>();

        // Register Root Component
        appBuilder.RootComponents.Add<Routes>("#app");

        var app = appBuilder.Build();

        // Start background sync loop
        StartBackgroundSync(app.Services);

        // Customize Native Window
        app.MainWindow
            .SetTitle("QuimeraReader")
            .SetSize(1280, 800)
            .SetUseOsDefaultSize(false)
            .Center();

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            Console.WriteLine($"[FATAL ERROR] {error.ExceptionObject}");
        };

        app.Run();
    }

    private static void StartBackgroundSync(IServiceProvider services)
    {
        Task.Run(async () =>
        {
            // Initial delay before first sync
            await Task.Delay(3000);
            while (true)
            {
                try
                {
                    var syncWorker = services.GetRequiredService<IOfflineSyncWorker>();
                    await syncWorker.SyncNowAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SyncLoop] {ex.Message}");
                }
                // Sync every 5 minutes
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        });
    }
}
