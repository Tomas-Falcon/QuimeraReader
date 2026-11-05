namespace QuimeraReader.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
#if ANDROID || WINDOWS
        blazorWebView.WebResourceRequested += QuimeraReader.Mobile.Services.MediaProxyInterceptor.OnWebResourceRequested;
#endif
#if ANDROID
        // El WebView de Android pinta blanco hasta que carga el HTML: se le da el fondo de la pantalla de arranque
        blazorWebView.HandlerChanged += (_, _) =>
        {
            if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView androidWebView)
            {
                androidWebView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#1A1A1A"));
            }
        };
#endif
    }

    protected override bool OnBackButtonPressed()
    {
#if ANDROID
        if (blazorWebView?.Handler?.PlatformView is Android.Webkit.WebView androidWebView && androidWebView.CanGoBack())
        {
            androidWebView.GoBack();
            return true;
        }
#endif
        return base.OnBackButtonPressed();
    }
}
