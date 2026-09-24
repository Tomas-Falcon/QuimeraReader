import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Look for AddScoped<ToastService>
    old_code = 'builder.Services.AddScoped<ToastService>();'
    new_code = 'builder.Services.AddScoped<ToastService>();\nbuilder.Services.AddSingleton<QuimeraReader.Shared.Interfaces.INetworkStateService, QuimeraReader.Web.Services.WebNetworkStateService>();'
    
    if old_code in content:
        content = content.replace(old_code, new_code)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Web/Program.cs')