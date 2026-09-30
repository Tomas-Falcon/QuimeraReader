using Microsoft.Extensions.Logging;
using Radzen;
#if ANDROID
using Android.Webkit;
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif

namespace QuimeraReader.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<QuimeraReader.Shared.Interfaces.INetworkStateService, QuimeraReader.Mobile.Services.MauiNetworkStateService>();

#if ANDROID
        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("AllowMixedContent", (handler, view) =>
        {
            handler.PlatformView.Settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        });
#endif


        builder.Services.AddRadzenComponents();


#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif
		
        builder.Services.AddScoped<QuimeraReader.Shared.Services.IServerConfigService, QuimeraReader.Mobile.Services.MobileServerConfigService>();
        
        builder.Services.AddScoped(sp => 
        {
            var config = sp.GetRequiredService<QuimeraReader.Shared.Services.IServerConfigService>();
            var url = config.ServerUrl ?? "http://localhost:5000/";
            return new HttpClient { BaseAddress = new Uri(url) };
        });

        builder.Services.AddScoped<QuimeraReader.Shared.Services.IBookService, QuimeraReader.Shared.Services.BookService>();
        builder.Services.AddScoped<QuimeraReader.Shared.Services.ISettingsService, QuimeraReader.Shared.Services.SettingsService>();
        builder.Services.AddScoped<QuimeraReader.Shared.Services.IScanService, QuimeraReader.Shared.Services.ScanService>();
        builder.Services.AddScoped<QuimeraReader.Shared.Interfaces.ITranslationService, QuimeraReader.Shared.Services.TranslationService>();
        builder.Services.AddScoped<QuimeraReader.Shared.Services.ToastService>();

        builder.Services.AddDbContext<QuimeraReader.Mobile.Data.LocalAppDbContext>();
        builder.Services.AddScoped<QuimeraReader.Shared.Interfaces.ILocalBookRepository, QuimeraReader.Mobile.Data.LocalBookRepository>();
        builder.Services.AddSingleton<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker, QuimeraReader.Mobile.Services.MauiOfflineSyncWorker>();


		return builder.Build();
	}
}