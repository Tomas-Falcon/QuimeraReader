namespace QuimeraReader.Shared.Interfaces;

/// <summary>
/// Sirve al WebView los archivos descargados para uso offline. El WebView no puede cargar rutas del disco en
/// &lt;audio src&gt;, así que el host nativo las expone por una URL propia con soporte de Range (saltos en el audio).
/// Solo existe en los clientes nativos que lo implementan.
/// </summary>
public interface ILocalMediaServer
{
    /// <summary>URL que el WebView puede cargar para ese archivo local, o null si no se puede servir</summary>
    string? GetUrl(string localFilePath);
}
