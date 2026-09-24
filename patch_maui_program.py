import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_text = '''        builder.Services.AddScoped<QuimeraReader.Shared.Services.ToastService>();'''
    new_text = '''        builder.Services.AddScoped<QuimeraReader.Shared.Services.ToastService>();

        builder.Services.AddDbContext<QuimeraReader.Mobile.Data.LocalAppDbContext>();
        builder.Services.AddScoped<QuimeraReader.Shared.Interfaces.ILocalBookRepository, QuimeraReader.Mobile.Data.LocalBookRepository>();
        builder.Services.AddSingleton<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker, QuimeraReader.Mobile.Services.MauiOfflineSyncWorker>();
'''
    content = content.replace(old_text, new_text)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Mobile/MauiProgram.cs')