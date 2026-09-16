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
		
        // En Android localhost es 10.0.2.2. En iOS/Windows es localhost.
        var baseUrl = DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5000/" : "http://localhost:5000/";
        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(baseUrl) });
        builder.Services.AddScoped<QuimeraReader.Shared.Services.IQuimeraApiClient, QuimeraReader.Shared.Services.QuimeraApiClient>();

		return builder.Build();
	}
}
