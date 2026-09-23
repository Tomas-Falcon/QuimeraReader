import os
file_path = 'c:/Users/tomas/Desktop/Proyectos/QuimeraReader/QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor'
with open(file_path, 'r', encoding='utf-8-sig') as f:
    content = f.read()

content = content.replace('@using QuimeraReader.Shared.Models', '@using QuimeraReader.Shared.Models\n@inject QuimeraReader.Shared.Interfaces.ITranslationService TranslationService')

replacements = [
    ('>Configuración del Servidor<', '>@TranslationService["Settings_ServerConfig"]<'),
    ('>Cargando preferencias...<', '>@TranslationService["Settings_LoadingPreferences"]<'),
    ('Text="General"', 'Text="@TranslationService[\"Settings_General\"]"'),
    (' /> Carpetas del Servidor<', ' /> @TranslationService["Settings_ServerFolders"]<'),
    ('>Define dónde buscará QuimeraReader los libros y cómo los importará.<', '>@TranslationService["Settings_ServerFoldersDesc"]<'),
    ('Text="Directorio de Origen (Descargas/Media)"', 'Text="@TranslationService[\"Settings_SourceDirectory\"]"'),
    ('>Ruta donde caen los EPUBs o MP3 sueltos (Volumen montado en Docker).<', '>@TranslationService["Settings_SourceDirectoryDesc"]<'),
    ('Text="Directorio de Salida (Librería Organizada)"', 'Text="@TranslationService[\"Settings_OutputDirectory\"]"'),
    ('>Ruta donde se guardarán y organizarán los libros y carátulas.<', '>@TranslationService["Settings_OutputDirectoryDesc"]<'),
    ('Text="Modo de Importación"', 'Text="@TranslationService[\"Settings_ImportMode\"]"'),
    ('>Mover y organizar o Dejar en lugar original.<', '>@TranslationService["Settings_ImportModeDesc"]<'),
    ('Text="Integraciones (API)"', 'Text="@TranslationService[\"Settings_IntegrationsAPI\"]"'),
    (' /> Inteligencia Artificial y Transcripción<', ' /> @TranslationService["Settings_AIAndTranscription"]<'),
    ('>Configura cómo deseas ejecutar los modelos de IA para sincronizar texto y audio.<', '>@TranslationService["Settings_AIAndTranscriptionDesc"]<'),
    ('Text="Modo de Ejecución de Whisper"', 'Text="@TranslationService[\"Settings_WhisperExecutionMode\"]"'),
    (' /> Configuración Local (Whisper.net)<', ' /> @TranslationService["Settings_LocalConfigWhisper"]<'),
    ('>Se descargarán los pesos del modelo directamente al contenedor. Depende del CPU/GPU del servidor.<', '>@TranslationService["Settings_LocalConfigWhisperDesc"]<'),
    ('Text="Ruta del Modelo Base (.bin)"', 'Text="@TranslationService[\"Settings_BaseModelPath\"]"'),
    ('Text="Descargar Ahora"', 'Text="@TranslationService[\"Settings_DownloadNow\"]"'),
    (' /> Configuración API Externa<', ' /> @TranslationService["Settings_ExternalAPIConfig"]<'),
    ('>Conecta QuimeraReader a OpenAI o a un modelo alojado en otro servidor (ej. Ollama).<', '>@TranslationService["Settings_ExternalAPIConfigDesc"]<'),
    ('Text="Endpoint URL"', 'Text="@TranslationService[\"Settings_EndpointURL\"]"'),
    ('Text="API Key"', 'Text="@TranslationService[\"Settings_APIKey\"]"'),
    (' /> Metadatos y Portadas<', ' /> @TranslationService["Settings_MetadataAndCovers"]<'),
    ('>Mejora la calidad de la biblioteca conectando con bases de datos externas.<', '>@TranslationService["Settings_MetadataAndCoversDesc"]<'),
    ('Text="Google Books API Key"', 'Text="@TranslationService[\"Settings_GoogleBooksAPIKey\"]"'),
    ('Text="Hardcover API Key"', 'Text="@TranslationService[\"Settings_HardcoverAPIKey\"]"'),
    ('Text="Sincronización de mi contenido (Hardcover)"', 'Text="@TranslationService[\"Settings_HardcoverSync\"]"'),
    ('Text="Tareas y Escaneo"', 'Text="@TranslationService[\"Settings_TasksAndScanning\"]"'),
    (' /> Sincronización Manual<', ' /> @TranslationService["Settings_ManualSync"]<'),
    ('>Fuerza a los Background Workers a ejecutar tareas de mantenimiento ahora mismo.<', '>@TranslationService["Settings_ManualSyncDesc"]<'),
    (' /> Escaneo en progreso...<', ' /> @TranslationService["Settings_ScanInProgress"]<'),
    ('Text="Cancelar"', 'Text="@TranslationService[\"Button_Cancel\"]"'),
    ('>Procesando: ', '>@TranslationService["Settings_Processing"] '),
    ('>Escanear Directorio Base<', '>@TranslationService["Settings_ScanBaseDirectory"]<'),
    ('>Busca nuevos archivos EPUB y MP3 en el volumen montado.<', '>@TranslationService["Settings_ScanBaseDirectoryDesc"]<'),
    ('Text="Iniciar"', 'Text="@TranslationService[\"Settings_Start\"]"'),
    ('>Reparación de Metadatos<', '>@TranslationService["Settings_MetadataRepair"]<'),
    ('>Busca portadas y sinopsis faltantes en Google Books y OpenLibrary.<', '>@TranslationService["Settings_MetadataRepairDesc"]<'),
    ('Text="Completar"', 'Text="@TranslationService[\"Settings_Complete\"]"'),
    ('>Limpieza de Duplicados<', '>@TranslationService["Settings_DuplicateCleanup"]<'),
    ('>Fusiona autores y categorías que se hayan guardado múltiples veces.<', '>@TranslationService["Settings_DuplicateCleanupDesc"]<'),
    ('Text="Fusionar"', 'Text="@TranslationService[\"Settings_Merge\"]"'),
    ('Text="Guardar Todos los Cambios"', 'Text="@TranslationService[\"Settings_SaveAllChanges\"]"')
]

for old, new in replacements:
    content = content.replace(old, new)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print('SettingsPanel updated')
