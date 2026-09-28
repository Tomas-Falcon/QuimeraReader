#!/bin/bash
# Script de actualización para QuimeraReader

echo "Iniciando proceso de actualización..."

# Si el contenedor tiene montado el socket de Docker (/var/run/docker.sock),
# puede hacer un pull de su propia imagen y reiniciarse a sí mismo.
# Para que esto funcione, el contenedor necesita tener el binario de docker o enviar peticiones a la API HTTP de docker.

# Intentaremos usar un curl al socket de docker para hacer pull de la imagen 
# asumiendo que la imagen se llame tomasfalcon/quimerareader:latest (o la que se haya configurado)
# NOTA: En la versión actual de docker-compose.yml el proyecto se construye localmente (build: .), 
# por lo que hacer pull no aplicará si no hay un registry remoto. 
# Este script está preparado para cuando se publique una imagen en Docker Hub o GHCR.

if [ -S /var/run/docker.sock ]; then
    echo "Docker socket detectado. Solicitando reinicio al host..."
    # A partir de aquí se pueden añadir los comandos curl a la API de docker local
    # Ejemplo genérico para reiniciar el contenedor actual si se supiera su ID:
    # curl --unix-socket /var/run/docker.sock -X POST http://localhost/containers/quimerareader/restart
    echo "El backend ahora se detendrá y Docker (gracias a restart:unless-stopped) volverá a iniciar el contenedor."
else
    echo "No se encontró el socket de Docker. Se detendrá la aplicación y se esperará a que el orquestador la reinicie."
fi

# El script termina aquí. El backend (SettingsController) se detendrá (StopApplication) un segundo después.
exit 0