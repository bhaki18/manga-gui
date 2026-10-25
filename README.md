# 🎬 Manga GUI - Modern Desktop Manga & Anime Hub

**Manga GUI** è un'interfaccia grafica desktop moderna, cross-platform e ultra-reattiva scritta in **C#** con **Avalonia UI** (v12) e pattern **MVVM**, progettata per offrire un'esperienza fluida e accattivante su **Linux** e **Windows** per guardare anime e leggere manga sia online che offline.

---

## 🐧 Installazione su Arch Linux / Manjaro (Pacman)

### Metodo 1: Installazione con pacman (.pkg.tar.zst già generato)
Puoi installare direttamente il pacchetto compilato eseguendo:
```bash
sudo pacman -U manga-gui-bin-1.0.0-1-x86_64.pkg.tar.zst
```

### Metodo 2: Compilazione locale tramite makepkg
```bash
git clone https://github.com/bhaki18/manga-gui.git
cd manga-gui
makepkg -si
```
Il pacchetto installerà:
- Il comando di sistema `/usr/bin/manga-gui`
- L'icona e il file `.desktop` nel menu applicazioni del tuo desktop environment (KDE, GNOME, XFCE, ecc.)

---

## ⌨️ Utilizzo da Riga di Comando (`manga-gui --FLAGS`)

Una volta installato, puoi lanciare `manga-gui` direttamente da terminale o specificare opzioni:

```bash
# Avvio normale (apre la schermata Anime predefinita)
manga-gui

# Apri direttamente la sezione Manga
manga-gui --manga
# oppure
manga-gui -m

# Apri direttamente la sezione Anime
manga-gui --anime
# oppure
manga-gui -a

# Apri direttamente i contenuti scaricati offline
manga-gui --downloaded
# oppure
manga-gui -d

# Cerca subito un titolo (anime o manga)
manga-gui -q "One Piece"
manga-gui --search "Berserk"

# Apri la cronologia degli episodi visti e capitoli letti
manga-gui -H
manga-gui --history

# Visualizza la guida ai comandi
manga-gui --help

# Visualizza la versione installata
manga-gui --version
```

---

## 🪟 Pacchetto e Build per Windows (Standalone `.exe`)

Per creare una versione standalone per Windows a 64-bit:
```bash
dotnet publish MangaGui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64
```
Troverai l'eseguibile autonomo pronto all'uso in:
`./publish/win-x64/MangaGui.exe` (con la cartella `cli/` inclusa accanto).

---

## ✨ Funzionalità

* 🎨 **Tema Scuro Nativo (Fluent Dark)**: Interfaccia raffinata con accenti arancioni e scuri, schede responsive e animazioni fluide.
* 🎬 **Anime Hub**: Navigazione e ricerca rapida nel catalogo con badge audio dinamici (`ITA`, `SUB ITA`, `ITA / SUB`).
* 📖 **Manga Hub**: Sfoglia migliaia di volumi con filtri lingua, generi e fallback intelligente multi-provider (One Piece Power, MangaDex, Mangapill).
* ⬇️ **Download Offline**: Scarica episodi anime e capitoli manga in locale (`~/.config/manga-gui/Downloaded/`) per visualizzarli anche senza connessione internet.
* ⚡ **Streaming e Lettura con MPV**: Riproduzione fluida a pieno schermo con gestione intelligente del referer per ogni provider e playlist automatica.
* 📜 **Cronologia e Segna come Visto/Letto**: Tracciamento dei capitoli ed episodi con intervalli compatti e menu di gestione con conferma.

---

## 🛠️ Tecnologie Utilizzate

* **Linguaggio**: C# (.NET 10)
* **UI Framework**: [Avalonia UI](https://avaloniaui.net/) (v12)
* **Architettura**: MVVM con `CommunityToolkit.Mvvm`
* **Player / Viewer**: [MPV](https://mpv.io/)
* **Motore dati**: Integrazione asincrona con `manga-cli`

---

## 📜 Licenza
Rilasciato sotto licenza [GPL-3.0-or-later](LICENSE).
