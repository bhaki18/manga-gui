#!/usr/bin/env bash
set -euo pipefail

# Posizionati sempre nella root del progetto
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PUSH_GH_PAGES=false

for arg in "$@"; do
    case "$arg" in
        --gh-pages|--push)
            PUSH_GH_PAGES=true
            ;;
        --help|-h)
            echo "Utilizzo: ./build-packages.sh [OPZIONI]"
            echo ""
            echo "Opzioni:"
            echo "  (nessuna)        Compila i binari Windows, l'installer Setup.exe e l'archivio .zip"
            echo "  --gh-pages       Pubblica automaticamente i pacchetti Windows sul branch gh-pages"
            echo "  --help, -h       Mostra questo messaggio"
            exit 0
            ;;
    esac
done

echo "============================================================"
echo "🚀 MANGA-GUI WINDOWS PACKAGE BUILDER"
echo "============================================================"

# 1. Verifica dipendenze Node per la CLI
if [ ! -d "./cli/node_modules" ]; then
    echo "📦 [1/3] Installazione dipendenze npm per cli/..."
    (cd ./cli && npm install)
else
    echo "✔ [1/3] Dipendenze CLI (node_modules) già presenti."
fi

# 2. Build Windows Standalone + Installer + ZIP
echo ""
echo "🪟 [2/3] Compilazione eseguibile Windows (win-x64)..."
dotnet publish MangaGui.csproj -c Release -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o ./publish/win-x64

# Rimuovi file debug PDB che eccedono il limite GitHub di 100MB
rm -f ./publish/win-x64/*.pdb

mkdir -p ./publish/win-x64/cli/src
cp -r ./cli/src/* ./publish/win-x64/cli/src/
cp -f ./Assets/manga-gui.ico ./publish/win-x64/manga-gui.ico
if [ -f ./version.json ]; then
    cp -f ./version.json ./publish/win-x64/version.json
fi

# Compila l'installer nativo MangaGui-Setup.exe con Go e icona incorporata
if [ -f ./installer/main.go ] && command -v go >/dev/null 2>&1; then
    (
        cd ./installer
        [ -x /home/adp/go/bin/rsrc ] && /home/adp/go/bin/rsrc -ico ../Assets/manga-gui.ico -o rsrc.syso
        GOOS=windows GOARCH=amd64 go build -ldflags="-s -w -H=windowsgui" -o "$SCRIPT_DIR/publish/win-x64/MangaGui-Setup.exe"
    )
    echo "✔ Installer nativo MangaGui-Setup.exe generato con successo."
fi

# Rimuovi file non necessari per rimanere sotto il limite GitHub (100MB)
rm -rf ./publish/win-x64/cli/node_modules/rxjs/dist/bundles \
       ./publish/win-x64/cli/node_modules/rxjs/dist/esm \
       ./publish/win-x64/cli/node_modules/rxjs/dist/esm5 \
       ./publish/win-x64/cli/node_modules/rxjs/dist/types \
       ./publish/win-x64/cli/node_modules/lodash/fp \
       ./publish/win-x64/cli/node_modules/axios/dist \
       ./publish/win-x64/cli/node_modules/.package-lock.json 2>/dev/null || true
find ./publish/win-x64/cli/node_modules -type d \( -name "test" -o -name "tests" -o -name "doc" -o -name "docs" -o -name "example" -o -name "examples" \) -exec rm -rf {} + 2>/dev/null || true
find ./publish/win-x64/cli -type f \( -name "*.d.ts" -o -name "*.d.ts.map" -o -name "*.md" -o -name "*.markdown" -o -name "*.map" -o -name "LICENSE*" -o -name "CHANGELOG*" \) -delete 2>/dev/null || true

# Rimuovi file .bat
rm -f ./publish/win-x64/*.bat

echo "✔ Eseguibile Windows pronto in ./publish/win-x64/MangaGui.exe"

echo ""
echo "🧰 [3/3] Creazione archivi Windows (.zip)..."
rm -f ./publish/manga-gui-windows.zip ./publish/manga-gui-win-x64.zip
TMP_WIN_DIR="./publish/manga-gui-win-x64"
rm -rf "$TMP_WIN_DIR"
ln -s win-x64 "$TMP_WIN_DIR"
(
    cd ./publish
    zip -9 -r -q manga-gui-windows.zip manga-gui-win-x64 -x "*.pdb"
    cp -f manga-gui-windows.zip manga-gui-win-x64.zip
)
rm -f "$TMP_WIN_DIR"

echo "✔ Archivi Windows aggiornati: ./publish/manga-gui-windows.zip e ./publish/manga-gui-win-x64.zip"

if [ "$PUSH_GH_PAGES" = true ]; then
    echo ""
    ./publish-gh-pages.sh
fi

echo ""
echo "============================================================"
echo "✅ COMPILAZIONE WINDOWS COMPLETATA CON SUCCESSO!"
echo "============================================================"
