using System.Collections.Concurrent;

namespace QuimeraReader.API.BackgroundServices;

/// <summary>
/// Ejecuta trabajos de mantenimiento largos fuera de la petición HTTP (una sola ejecución por trabajo a la vez)
/// y guarda en memoria su progreso y su último resultado.
/// </summary>
public class MaintenanceJobRunner
{
    private class JobState
    {
        public bool Running { get; set; }
        public object? Options { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int Progress { get; set; }
        public string? Error { get; set; }
        public object? Result { get; set; }
    }

    public delegate Task<object> Job(IServiceProvider services, Action<int> reportProgress, CancellationToken cancellationToken);

    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MaintenanceJobRunner> _logger;
    private readonly ConcurrentDictionary<string, JobState> _jobs = new();

    public MaintenanceJobRunner(IServiceProvider serviceProvider, IHostApplicationLifetime lifetime, ILogger<MaintenanceJobRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
        _logger = logger;
    }

    public object GetStatus(string name)
    {
        var state = _jobs.GetOrAdd(name, _ => new JobState());
        lock (state)
        {
            return new { Running = state.Running, state.Options, state.StartedAt, state.FinishedAt, state.Progress, state.Error, state.Result };
        }
    }

    public bool TryStart(string name, object? options, Job job)
    {
        var state = _jobs.GetOrAdd(name, _ => new JobState());
        lock (state)
        {
            if (state.Running) return false;
            state.Running = true;
            state.Options = options;
            state.StartedAt = DateTime.UtcNow;
            state.FinishedAt = null;
            state.Progress = 0;
            state.Error = null;
        }

        _ = Task.Run(async () =>
        {
            object? result = null;
            string? error = null;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                result = await job(scope.ServiceProvider, p => { lock (state) state.Progress = p; }, _lifetime.ApplicationStopping);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogError(ex, "Error en el trabajo de mantenimiento {Job}", name);
            }
            finally
            {
                lock (state)
                {
                    state.Running = false;
                    state.FinishedAt = DateTime.UtcNow;
                    state.Error = error;
                    if (result != null) state.Result = result;
                }
            }
        });
        return true;
    }
}
