using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

public class ToastService
{
    public event Action<string, string>? OnShow;

    /// <summary>Aviso con un botón (p. ej. "Deshacer"). Se queda más tiempo en pantalla que uno normal.</summary>
    public event Action<string, string, string, Func<Task>>? OnShowWithAction;

    public void ShowSuccess(string message) => OnShow?.Invoke(message, "success");
    public void ShowError(string message) => OnShow?.Invoke(message, "error");
    public void ShowInfo(string message) => OnShow?.Invoke(message, "info");

    public void ShowWithAction(string message, string actionText, Func<Task> action, string type = "info") =>
        OnShowWithAction?.Invoke(message, type, actionText, action);
}
