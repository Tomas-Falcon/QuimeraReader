#!/bin/sh
# Registra QuimeraReader en el menú de aplicaciones del usuario, con su icono.
# En Wayland el escritorio toma el icono de este archivo .desktop, no de la ventana.
# Uso: ./install.sh          (instala)    ./install.sh --remove   (quita)
set -e

APP_DIR="$(cd "$(dirname "$0")" && pwd)"
APPS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
# El nombre del archivo coincide con el identificador de la ventana (el nombre del ejecutable)
DESKTOP_FILE="$APPS_DIR/QuimeraReader.Desktop.desktop"

if [ "$1" = "--remove" ]; then
    rm -f "$DESKTOP_FILE"
    echo "Acceso directo quitado."
    exit 0
fi

chmod +x "$APP_DIR/QuimeraReader.Desktop"
mkdir -p "$APPS_DIR"
cat > "$DESKTOP_FILE" <<DESKTOP
[Desktop Entry]
Type=Application
Name=QuimeraReader
Comment=Lector de libros y audiolibros
Exec="$APP_DIR/QuimeraReader.Desktop"
Path=$APP_DIR
Icon=$APP_DIR/wwwroot/favicon.png
Terminal=false
Categories=Office;Viewer;AudioVideo;
StartupWMClass=QuimeraReader.Desktop
DESKTOP

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
echo "QuimeraReader instalado en el menú de aplicaciones: $DESKTOP_FILE"
