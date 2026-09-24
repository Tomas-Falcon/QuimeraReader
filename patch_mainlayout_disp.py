import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    if '@implements IDisposable' not in content:
        content = content.replace('@inject QuimeraReader.Shared.Interfaces.INetworkStateService NetworkStateService', '@inject QuimeraReader.Shared.Interfaces.INetworkStateService NetworkStateService\n@implements IDisposable')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor')