using Microsoft.Extensions.Logging;

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

        builder.Services.AddScoped<QuimeraReader.Shared.Services.IQuimeraApiClient, QuimeraReader.Shared.Services.QuimeraApiClient>();
        builder.Services.AddScoped<QuimeraReader.Shared.Services.ToastService>();

		return builder.Build();
	}
}
