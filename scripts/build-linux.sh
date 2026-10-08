#!/usr/bin/env bash
# Script di compilazione e packaging per Linux (linux-x64).
set -euo pipefail

VERSION="${1:-0.6.0}"
CONFIGURATION="${2:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_FILE="${ROOT_DIR}/src/Paddock.UI/Paddock.UI.csproj"
UPDATER_PROJECT_FILE="${ROOT_DIR}/src/Paddock.Updater/Paddock.Updater.csproj"
ARTIFACTS_DIR="${ROOT_DIR}/artifacts"
LINUX_TEMP_DIR="${ARTIFACTS_DIR}/linux-temp"
PUBLISH_DIR="${LINUX_TEMP_DIR}/publish"
ZIP_FILE_NAME="Paddock-${VERSION}-linux-x64.zip"
ZIP_FILE_PATH="${ARTIFACTS_DIR}/${ZIP_FILE_NAME}"

echo "===================================================="
echo " Paddock — Linux Packaging (linux-x64)"
echo " Versione:       ${VERSION}"
echo " Configurazione: ${CONFIGURATION}"
echo " Root:           ${ROOT_DIR}"
echo "===================================================="

# 1. Pulizia output precedenti
rm -rf "${LINUX_TEMP_DIR}"
rm -f "${ZIP_FILE_PATH}"
mkdir -p "${ARTIFACTS_DIR}"
mkdir -p "${PUBLISH_DIR}"

# 2. Compilazione e pubblicazione nativa self-contained
echo "[1/4] Esecuzione dotnet publish Paddock.UI (linux-x64, self-contained)..."
dotnet publish "${PROJECT_FILE}" \
    -c "${CONFIGURATION}" \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:Version="${VERSION}" \
    -p:AssemblyVersion="${VERSION}.0" \
    -p:FileVersion="${VERSION}.0" \
    -o "${PUBLISH_DIR}"

echo "[1b/4] Esecuzione dotnet publish Paddock.Updater (linux-x64, self-contained)..."
dotnet publish "${UPDATER_PROJECT_FILE}" \
    -c "${CONFIGURATION}" \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:Version="${VERSION}" \
    -p:AssemblyVersion="${VERSION}.0" \
    -p:FileVersion="${VERSION}.0" \
    -o "${PUBLISH_DIR}"

# 3. Assegnazione permessi di esecuzione ai binari ELF
echo "[2/4] Verifica e configurazione permessi di esecuzione..."
EXECUTABLE="${PUBLISH_DIR}/Paddock.UI"
if [ ! -f "${EXECUTABLE}" ]; then
    echo "ERRORE: Eseguibile non trovato in ${EXECUTABLE}" >&2
    exit 1
fi
chmod +x "${EXECUTABLE}"

UPDATER_EXECUTABLE="${PUBLISH_DIR}/Paddock.Updater"
if [ ! -f "${UPDATER_EXECUTABLE}" ]; then
    echo "ERRORE: Eseguibile non trovato in ${UPDATER_EXECUTABLE}" >&2
    exit 1
fi
chmod +x "${UPDATER_EXECUTABLE}"

# Copia icone e file desktop per integrazione Linux
[ -f "${ROOT_DIR}/logo.png" ] && cp "${ROOT_DIR}/logo.png" "${PUBLISH_DIR}/logo.png"
[ -f "${ROOT_DIR}/logo.jpg" ] && cp "${ROOT_DIR}/logo.jpg" "${PUBLISH_DIR}/logo.jpg"
[ -f "${ROOT_DIR}/paddock.desktop" ] && cp "${ROOT_DIR}/paddock.desktop" "${PUBLISH_DIR}/paddock.desktop"

# 4. Creazione archivio ZIP con preservazione dei permessi
echo "[3/4] Compressione archivio ZIP: ${ZIP_FILE_NAME}..."
if command -v zip >/dev/null 2>&1; then
    (
        cd "${PUBLISH_DIR}"
        zip -r "${ZIP_FILE_PATH}" ./*
    )
elif command -v python3 >/dev/null 2>&1; then
    (
        cd "${PUBLISH_DIR}"
        python3 -m zipfile -c "${ZIP_FILE_PATH}" ./*
    )
else
    echo "ERRORE: Ne 'zip' ne 'python3' sono disponibili per creare l'archivio ZIP." >&2
    exit 1
fi

echo "[4/4] Pacchetto creato con successo!"
echo " Percorso: ${ZIP_FILE_PATH}"
ls -lh "${ZIP_FILE_PATH}"
echo "===================================================="

# Pulizia staging
rm -rf "${LINUX_TEMP_DIR}"

