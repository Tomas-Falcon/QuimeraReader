import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_button = '''<RadzenButton Text="@TranslationService["Settings_DownloadNow"]" Click="DownloadWhisperModelAsync" IsBusy="@_isDownloadingWhisper" ButtonStyle="Radzen.ButtonStyle.Primary" Variant="Radzen.Variant.Outlined" />'''
    new_button = '''@if (_isWhisperDownloaded) {
                                                <RadzenBadge BadgeStyle="Radzen.BadgeStyle.Success" Text="Modelo Listo" class="py-2 px-3 align-self-center" />
                                            } else {
                                                <RadzenButton Text="@TranslationService["Settings_DownloadNow"]" Click="DownloadWhisperModelAsync" IsBusy="@_isDownloadingWhisper" ButtonStyle="Radzen.ButtonStyle.Primary" Variant="Radzen.Variant.Outlined" />
                                            }'''
    content = content.replace(old_button, new_button)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor')