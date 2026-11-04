using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace QuimeraReader.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource _cts = new();
    private bool _isForeground = true;

	public App(IServiceProvider serviceProvider)
	{
		InitializeComponent();
        _serviceProvider = serviceProvider;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "QuimeraReader.Mobile" };
        
        window.Created += (s, e) => StartSyncLoop();
        window.Resumed += (s, e) => {
            _isForeground = true;
            TriggerSyncFireAndForget();
        };
        window.Deactivated += (s, e) => _isForeground = false;
        
        return window;
	}

    private void TriggerSyncFireAndForget()
    {
        Task.Run(async () => {
            try {
                var syncWorker = _serviceProvider.GetRequiredService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
                await syncWorker.SyncNowAsync();
            } catch { }
        });
    }

    private void StartSyncLoop()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () => {
            // Diferir la primera sincronización para no competir con el arranque de la UI (render de la biblioteca)
            try { await Task.Delay(TimeSpan.FromSeconds(8), token); } catch (TaskCanceledException) { return; }
            TriggerSyncFireAndForget();

            while (!token.IsCancellationRequested)
            {
                int waitMinutes = _isForeground ? 10 : 20;
                
                try {
                    await Task.Delay(TimeSpan.FromMinutes(waitMinutes), token);
                } catch (TaskCanceledException) {
                    break;
                }

                if (!token.IsCancellationRequested)
                {
                    TriggerSyncFireAndForget();
                }
            }
        }, token);
    }
}
