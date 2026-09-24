param (
    [string]$OutputPath = "C:\Users\tomas\Desktop\Varios\APKs Proyecto Quimera\QuimeraReader_Latest.apk"
)

Write-Host "Compilando QuimeraReader.Mobile para Android (APK)..."
dotnet publish QuimeraReader.Clients/QuimeraReader.Mobile/QuimeraReader.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk

$apkSource = "QuimeraReader.Clients/QuimeraReader.Mobile/bin/Release/net10.0-android/publish/com.companyname.quimerareader.mobile-Signed.apk"

if (Test-Path $apkSource) {
    Copy-Item $apkSource -Destination $OutputPath -Force
    Write-Host "APK guardado exitosamente en: $OutputPath" -ForegroundColor Green
} else {
    Write-Host "No se encontro el APK generado en la ruta esperada: $apkSource" -ForegroundColor Red
}