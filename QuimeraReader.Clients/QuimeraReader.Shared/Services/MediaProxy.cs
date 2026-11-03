namespace QuimeraReader.Shared.Services;

/// <summary>
/// Ruta del propio origen de la app que el host nativo (MAUI Android) intercepta y reenvía al servidor.
/// El WebView corre en https://0.0.0.1 y bloquea por "mixed content" las imágenes/audio servidos por http://,
/// mientras que una ruta relativa del mismo origen nunca se considera contenido mixto.
/// </summary>
public static class MediaProxy
{
    public const string PathPrefix = "/_qrmedia/";
}
