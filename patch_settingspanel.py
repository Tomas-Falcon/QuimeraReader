import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Inject NetworkStateService
    content = content.replace('@inject QuimeraReader.Shared.Interfaces.ITranslationService TranslationService', '@inject QuimeraReader.Shared.Interfaces.ITranslationService TranslationService\n@inject QuimeraReader.Shared.Interfaces.INetworkStateService NetworkStateService')

    # Add switch in General Tab
    general_tab = '''<RadzenCard class=""mb-3 bg-dark text-white border-0"">
                    <h5 class=""mb-3"">@TranslationService[""Settings_Theme""]</h5>
                    <div class=""d-flex align-items-center mb-3"">
                        <RadzenSwitch @bind-Value=""_isDarkMode"" Change=""OnThemeChanged"" />
                        <span class=""ms-3"">@(_isDarkMode ? TranslationService[""Settings_DarkTheme""] : TranslationService[""Settings_LightTheme""])</span>
                    </div>

                    <h5 class=""mb-3 mt-4"">Modo Sin Conexión</h5>
                    <div class=""d-flex align-items-center mb-3"">
                        <RadzenSwitch @bind-Value=""_isForceOffline"" Change=""OnForceOfflineChanged"" />
                        <span class=""ms-3"">Forzar Modo Sin Conexión (Ahorro de datos)</span>
                    </div>
                </RadzenCard>'''
                
    old_general_tab = '''<RadzenCard class=""mb-3 bg-dark text-white border-0"">
                    <h5 class=""mb-3"">@TranslationService[""Settings_Theme""]</h5>
                    <div class=""d-flex align-items-center mb-3"">
                        <RadzenSwitch @bind-Value=""_isDarkMode"" Change=""OnThemeChanged"" />
                        <span class=""ms-3"">@(_isDarkMode ? TranslationService[""Settings_DarkTheme""] : TranslationService[""Settings_LightTheme""])</span>
                    </div>
                </RadzenCard>'''
                
    content = content.replace(old_general_tab, general_tab)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor')