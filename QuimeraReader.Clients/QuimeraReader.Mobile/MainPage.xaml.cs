namespace QuimeraReader.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
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
