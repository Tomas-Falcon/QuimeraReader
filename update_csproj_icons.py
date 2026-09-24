import sys

path = 'QuimeraReader.Clients/QuimeraReader.Mobile/QuimeraReader.Mobile.csproj'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    '<MauiIcon Include=\"Resources\\AppIcon\\appicon.svg\" ForegroundFile=\"Resources\\AppIcon\\appiconfg.svg\" Color=\"#512BD4\" />',
    '<MauiIcon Include=\"Resources\\AppIcon\\appicon.png\" />'
)

content = content.replace(
    '<MauiSplashScreen Include=\"Resources\\Splash\\splash.svg\" Color=\"#512BD4\" BaseSize=\"128,128\" />',
    '<MauiSplashScreen Include=\"Resources\\Splash\\splash_logo.png\" Color=\"#1a1a1a\" BaseSize=\"128,128\" />'
)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)