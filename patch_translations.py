import sys
import json

def update_json(filepath, new_keys):
    with open(filepath, 'r', encoding='utf-8-sig') as f:
        data = json.load(f)
    
    for k, v in new_keys.items():
        data[k] = v
        
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

es_keys = {
    'Annotation_New': 'Nueva Anotación',
    'Annotation_Color': 'Color de resaltado',
    'Annotation_NoColor': 'Sin color (Solo subrayado)',
    'Annotation_NoteTitle': 'Nota (Opcional)',
    'Annotation_NotePlaceholder': 'Escribe un comentario...',
    'Action_Cancel': 'Cancelar',
    'Action_Save': 'Guardar',
    'Action_PlayFromHere': 'Reproducir',
    'Message_PlayingFromSelection': 'Reproduciendo desde la selección.',
    'Error_AudioNotFoundForSelection': 'No se encontró el audio para este fragmento.',
    'Message_AnnotationSaved': 'Anotación guardada.',
    'Error_SavingAnnotation': 'Error guardando anotación:'
}

en_keys = {
    'Annotation_New': 'New Annotation',
    'Annotation_Color': 'Highlight Color',
    'Annotation_NoColor': 'No color (Underline only)',
    'Annotation_NoteTitle': 'Note (Optional)',
    'Annotation_NotePlaceholder': 'Write a comment...',
    'Action_Cancel': 'Cancel',
    'Action_Save': 'Save',
    'Action_PlayFromHere': 'Play',
    'Message_PlayingFromSelection': 'Playing from selection.',
    'Error_AudioNotFoundForSelection': 'Audio not found for this segment.',
    'Message_AnnotationSaved': 'Annotation saved.',
    'Error_SavingAnnotation': 'Error saving annotation:'
}

update_json('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/Translations/es.json', es_keys)
update_json('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/Translations/en.json', en_keys)