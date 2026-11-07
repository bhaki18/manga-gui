#!/usr/bin/env bash
set -euo pipefail

# Posizionati sempre nella root del progetto
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

REPO_URL="git@github.com:bhaki18/manga-cli.git"
BRANCH="gh-pages"
TMP_DIR="$SCRIPT_DIR/.gh-pages-tmp"
DO_BUILD=false
NEW_PKGVER=""
CUSTOM_NOTES=""
CUSTOM_MSG=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --build|-b)
            DO_BUILD=true
            shift
            ;;
        --version|-v)
            NEW_PKGVER="${2:-}"
            DO_BUILD=true
            shift 2
            ;;
        --notes|-n)
            CUSTOM_NOTES="${2:-}"
            shift 2
            ;;
        -m|--message)
            CUSTOM_MSG="${2:-}"
            shift 2
            ;;
        --help|-h)
            echo "Utilizzo: ./publish-gh-pages.sh [OPZIONI]"
            echo ""
            echo "Pubblica i pacchetti Windows sul branch 'gh-pages' di bhaki18/manga-cli.git."
            echo ""
            echo "Opzioni:"
            echo "  (nessuna)              Pubblica i pacchetti Windows (windows/) e version.json"
            echo "  --build, -b            Ricompila prima i pacchetti tramite ./build-packages.sh"
            echo "  --version, -v <ver>    Imposta una nuova versione (es. 1.2.7), ricompila e pubblica"
            echo "  --notes, -n <testo>    Imposta le note di rilascio mostrate nel popup dell'Updater agli utenti"
            echo "  -m, --message <msg>    Usa un messaggio di commit personalizzato"
            echo "  --help, -h             Mostra questo messaggio"
            exit 0
            ;;
        *)
            echo "Opzione non riconosciuta: $1 (usa --help per la guida)"
            exit 1
            ;;
    esac
done

echo "============================================================"
echo "🌐 MANGA-GUI WINDOWS -> GH-PAGES PUBLISHER"
echo "   Repo:   $REPO_URL (branch: $BRANCH)"
echo "============================================================"

# 1. Eventuale aggiornamento versione in version.json
if [ -n "$NEW_PKGVER" ] && [ -f "./version.json" ] && command -v node >/dev/null 2>&1; then
    node -e '
      const fs = require("fs");
      const v = JSON.parse(fs.readFileSync("./version.json", "utf8"));
      v.version = process.argv[1];
      v.pkgver = process.argv[1].split("-")[0];
      v.releaseDate = new Date().toISOString().slice(0, 10);
      fs.writeFileSync("./version.json", JSON.stringify(v, null, 2) + "\n");
    ' "$NEW_PKGVER"
    echo "🔼 Impostata nuova versione in version.json: $NEW_PKGVER"
fi

if [ -n "$CUSTOM_NOTES" ] && [ -f "./version.json" ] && command -v node >/dev/null 2>&1; then
    node -e '
      const fs = require("fs");
      const v = JSON.parse(fs.readFileSync("./version.json", "utf8"));
      v.notes = process.argv[1];
      fs.writeFileSync("./version.json", JSON.stringify(v, null, 2) + "\n");
    ' "$CUSTOM_NOTES"
fi

# 2. Eventuale compilazione
if [ "$DO_BUILD" = true ]; then
    echo "🔨 Avvio compilazione pacchetto Windows..."
    ./build-packages.sh
fi

LATEST_WIN=$(ls -t publish/manga-gui-windows.zip publish/manga-gui-win-x64.zip 2>/dev/null | head -n 1 || true)
if [ -z "$LATEST_WIN" ] || [ ! -f "$LATEST_WIN" ]; then
    echo "⚠️ Nessun pacchetto Windows ZIP trovato in publish/. Avvio compilazione..."
    ./build-packages.sh
fi

# 3. Clone / checkout gh-pages
rm -rf "$TMP_DIR"
echo "📥 Clonazione gh-pages..."
git clone --depth 1 -b "$BRANCH" "$REPO_URL" "$TMP_DIR"

mkdir -p "$TMP_DIR/windows"
rm -f "$TMP_DIR"/windows/*.zip
cp -f publish/manga-gui-windows.zip "$TMP_DIR/windows/"
cp -f publish/manga-gui-windows.zip "$TMP_DIR/windows/manga-gui-win-x64.zip"
if [ -f publish/win-x64/MangaGui-Setup.exe ]; then
    cp -f publish/win-x64/MangaGui-Setup.exe "$TMP_DIR/windows/"
fi

if [ -f "./version.json" ]; then
    cp -f "./version.json" "$TMP_DIR/version.json"
fi

cd "$TMP_DIR"
git add windows/ version.json

if git diff --staged --quiet; then
    echo "ℹ️ Nessuna modifica da pubblicare su gh-pages."
    cd "$SCRIPT_DIR"
    rm -rf "$TMP_DIR"
    exit 0
fi

CURRENT_VER=$(node -e 'try { console.log(require("./version.json").version); } catch { console.log(""); }' 2>/dev/null || echo "latest")
COMMIT_MSG="${CUSTOM_MSG:-"chore(release): publish Windows package v${CURRENT_VER}"}"

echo "💾 Esecuzione commit su gh-pages: $COMMIT_MSG"
git commit -m "$COMMIT_MSG"

echo "🚀 Invio modifiche su gh-pages (git push)..."
git push origin "$BRANCH"

cd "$SCRIPT_DIR"
rm -rf "$TMP_DIR"

echo "============================================================"
echo "🎉 PUBBLICAZIONE COMPLETATA CON SUCCESSO SU GH-PAGES!"
echo "============================================================"
