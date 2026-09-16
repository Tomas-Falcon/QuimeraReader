# QuimeraReader 📖🎧

**QuimeraReader** es un backend ligero y moderno desarrollado en **.NET 10 (ASP.NET Core)** bajo los principios de Clean Architecture. Su propósito principal es actuar como un servidor personal (Homelab) para gestionar bibliotecas masivas de libros electrónicos (EPUB) y audiolibros, integrando funciones innovadoras de sincronización mediante Inteligencia Artificial.

## ✨ Características Principales

- **Alineación de Audio a Texto (Read-Along):** Utiliza `Whisper.net` para generar mapas de sincronización (SyncMaps) locales, permitiendo una experiencia fluida entre leer y escuchar.
- **Scraping de Metadatos:** Integración automática con **Google Books** y **Open Library** para enriquecer tu biblioteca con portadas, autores y sinopsis.
- **Optimizaciones para Homelabs:** Diseñado para manejar miles de libros con paginación (`Infinite Scroll`), *Lazy Loading* de portadas y bajo consumo de memoria.
- **Streaming Nativo de Media:** Endpoints optimizados para servir EPUBs y realizar *streaming* parcial de audiolibros (`206 Partial Content`), permitiendo saltar a cualquier minuto sin descargar el archivo completo.
- **Procesamiento en Segundo Plano:** Escaneo de carpetas y alineación de audio delegados a *Background Workers* para no bloquear la API.

## 🏗️ Arquitectura

El proyecto sigue una estructura limpia de separación de responsabilidades:
- **`QuimeraReader.API`**: Controladores REST, Endpoints de Media y Background Services.
- **`QuimeraReader.Application`**: Casos de uso e interfaces (CQRS preparado).
- **`QuimeraReader.Domain`**: Entidades core del negocio (`Book`, `Author`, `SyncMap`).
- **`QuimeraReader.Infrastructure`**: Implementación de base de datos (SQLite), colas y proveedores externos.

## 🚀 Instalación y Uso

1. Clona el repositorio:
   ```bash
   git clone https://github.com/Tomas-Falcon/QuimeraReader.git
   ```
2. Restaura los paquetes y compila:
   ```bash
   dotnet build
   ```
3. Ejecuta la API:
   ```bash
   cd QuimeraReader.API
   dotnet run
   ```
La API creará automáticamente la base de datos local SQLite (`quimerareader.db`) al iniciar.

## 🔌 Compatibilidad Frontend
QuimeraReader está diseñado para ser consumido por clientes compatibles con estándares de lectura (como los adaptados de *Storyteller*). Exponemos URLs directas y paginadas de los recursos multimedia y un `AuthController` *mockeado* para facilitar la integración rápida con aplicaciones React Native / Expo de terceros.

---

## 🔎 Análisis Técnico de Componentes (Settings & Navegación)

En preparación para agregar la pantalla de Administración (AdminSettings), a continuación se presenta un análisis de cómo interactúan las distintas capas de Backend y Frontend:

### 1. Backend (Controladores y Base de Datos)
QuimeraReader *no* utiliza MediatR/CQRS explícitamente en los controladores de API para evitar sobreingeniería innecesaria. En su lugar, inyecta directamente el `AppDbContext` para operaciones CRUD sencillas, como se ve en `SettingsController.cs`:

```csharp
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public SettingsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _dbContext.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(settings);
    }
    // ... SaveSetting (POST) omitido para brevedad ...
}
```

La tabla en SQLite (`SystemSettings`) está mapeada a un modelo muy sencillo de Clave-Valor en `QuimeraReader.Domain/Entities/SystemSetting.cs`:
```csharp
public class SystemSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
```
Esto permite guardar *CUALQUIER* configuración futura sin tener que migrar la base de datos constantemente.

### 2. Frontend (Capa de Red - RTK Query)
Ambas interfaces (Móvil y Web) utilizan **Redux Toolkit Query** (`RTK Query`) para interactuar con la API de Quimera.

**Web (`api.ts`):** 
Usa un `fetchBaseQuery` apuntando al endpoint v2 o a nuestro backend. La configuración principal incluye auto-invalidación de caché mediante *Tags* (`tagTypes`).
```typescript
export const api = createApi({
  reducerPath: "api",
  baseQuery: fetchBaseQuery({ baseUrl: "/api/v2" }),
  tagTypes: ["Books", "UserSettings", ...], // etc.
  endpoints: (build) => ({ /* ... */ })
});
```

**Mobile (`serverApi.ts`):** 
Similar, pero maneja persistencia de tokens de seguridad y la URL base dinámica de nuestro QuimeraReader. Nosotros ya interceptamos `transformResponse` para traducir nuestra paginación (`{ total, page, data }`) a los datos que el state-manager espera.

### 3. Frontend (Navegación e Inyección de Vistas)
Si decidimos anclar una nueva pantalla de `AdminSettings`, estos son los enrutadores que utilizaremos:

**React Native - Móvil (Expo Router):**
Las vistas se declaran estáticamente en `app/(root)/_layout.tsx` dentro de un `<Stack>`:
```tsx
<Stack>
  <Stack.Screen name="index" />
  <Stack.Screen name="settings" options={{ title: "Settings" }} />
  {/* Aquí anclaríamos: <Stack.Screen name="admin-settings" /> */}
  <Stack.Screen name="book/[uuid]" />
</Stack>
```
El nuevo panel podría vivir en `app/(root)/admin-settings.tsx`.

**React - Web (Next.js App Router):**
La web usa Next.js 14+ con App Router (`app/(v2)/layout.tsx`), que engloba toda la aplicación en `MantineProvider` y `StoreProvider`. Crear la vista de admin solo requerirá crear la carpeta `app/(v2)/(dashboard)/admin/page.tsx` para aprovechar el Layout principal.
