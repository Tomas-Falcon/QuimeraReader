using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

public class ToastService
{
    public event Action<string, string>? OnShow;

    public void ShowSuccess(string message) => OnShow?.Invoke(message, "success");
    public void ShowError(string message) => OnShow?.Invoke(message, "error");
    public void ShowInfo(string message) => OnShow?.Invoke(message, "info");
}
