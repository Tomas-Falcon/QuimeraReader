import sys
import re

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Translations
    content = content.replace('<h6 class=""mb-0 text-white""><i class=""bi bi-pencil-square me-2""></i> Nueva Anotación</h6>', '<h6 class=""mb-0 text-white""><i class=""bi bi-pencil-square me-2""></i> @TranslationService[""Annotation_New""]</h6>')
    content = content.replace('<label class=""form-label text-white-50 small"">Color de resaltado</label>', '<label class=""form-label text-white-50 small"">@TranslationService[""Annotation_Color""]</label>')
    content = content.replace('title=""Sin color (Solo subrayado)""', 'title=""@TranslationService[""Annotation_NoColor""]""')
    content = content.replace('<label class=""form-label text-white-50 small"">Nota (Opcional)</label>', '<label class=""form-label text-white-50 small"">@TranslationService[""Annotation_NoteTitle""]</label>')
    content = content.replace('placeholder=""Escribe un comentario...""', 'placeholder=""@TranslationService[""Annotation_NotePlaceholder""]""')
    content = content.replace('<button class=""btn btn-secondary btn-sm"" @onclick=""CloseAnnotationDialog"">Cancelar</button>', '<button class=""btn btn-secondary btn-sm"" @onclick=""CloseAnnotationDialog"">@TranslationService[""Action_Cancel""]</button>')
    content = content.replace('<button class=""btn btn-primary btn-sm"" @onclick=""SaveAnnotationAsync"">Guardar</button>', '<button class=""btn btn-primary btn-sm"" @onclick=""SaveAnnotationAsync"">@TranslationService[""Action_Save""]</button>')
    content = content.replace('<button class=""btn btn-info btn-sm text-white"" @onclick=""PlayFromSelectionAsync""><i class=""bi bi-play-circle me-1""></i> Reproducir</button>', '<button class=""btn btn-info btn-sm text-white"" @onclick=""PlayFromSelectionAsync""><i class=""bi bi-play-circle me-1""></i> @TranslationService[""Action_PlayFromHere""]</button>')

    # Code block translations and timing offset
    content = content.replace('ToastService.ShowSuccess(""Reproduciendo desde la selección."");', 'ToastService.ShowSuccess(TranslationService[""Message_PlayingFromSelection""]);')
    content = content.replace('ToastService.ShowError(""No se encontró el audio para este fragmento."");', 'ToastService.ShowError(TranslationService[""Error_AudioNotFoundForSelection""]);')
    content = content.replace('ToastService.ShowSuccess(""Anotación guardada."");', 'ToastService.ShowSuccess(TranslationService[""Message_AnnotationSaved""]);')
    content = content.replace('ToastService.ShowError($""Error guardando anotación: {ex.Message}"");', 'ToastService.ShowError($""{TranslationService[\""Error_SavingAnnotation\""]} {ex.Message}"");')

    # SeekToSegmentAsync offset
    content = content.replace('await SeekToSegmentAsync(match.Start);', 'var startOffset = Math.Max(0, match.Start - 1.5);\n                await SeekToSegmentAsync(startOffset);')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')