# 🎬 Manga GUI - Modern Desktop Manga & Anime Hub (Windows)

**Manga GUI** is a modern, ultra-responsive Windows desktop client built with **C#**, **Avalonia UI** (v12), and the **MVVM** pattern, designed for streaming anime and reading manga online and offline.

> 🐧 **Linux User?** Head over to the dedicated repository for Linux packages (Arch Linux, Debian/Ubuntu, and Universal Tarball): [manga-gui-linux](https://github.com/bhaki18/manga-gui-linux).
> 📱 **Android User?** Head over to the dedicated repository for Android mobile: [manga-gui-mobile](https://github.com/bhaki18/manga-gui-mobile).

---

## 🪟 Installation on Windows

### Option 1: Native Setup Installer (Recommended)
Download `MangaGui-Setup.exe` from the latest release or website:
1. Run `MangaGui-Setup.exe`.
2. Follow the setup wizard to install into your local user programs.
3. Launch **Manga GUI** from your Start Menu or Desktop shortcut!

### Option 2: Portable ZIP
Download `manga-gui-windows.zip`:
1. Extract the `.zip` archive anywhere on your disk.
2. Double-click `MangaGui.exe` to run.

---

## ⌨️ Command Line Usage (`MangaGui.exe [flags]`)

You can launch Manga GUI with custom startup options:

```bash
# Standard startup
MangaGui.exe

# Open directly into Manga section
MangaGui.exe --manga
MangaGui.exe -m

# Open directly into Anime section
MangaGui.exe --anime
MangaGui.exe -a

# Open offline downloads
MangaGui.exe --downloaded
MangaGui.exe -d

# Search for a title immediately
MangaGui.exe -q "One Piece"
MangaGui.exe --search "Berserk"

# Open watch/read history
MangaGui.exe -H
MangaGui.exe --history

# Help and Version
MangaGui.exe --help
MangaGui.exe --version
```

---

## 🛠️ Building From Source

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js (LTS)](https://nodejs.org/) & `npm`
- [Go](https://go.dev/) (optional, required only to build `MangaGui-Setup.exe`)
- `git`

### Build Command
```bash
./build-packages.sh
```
This generates:
- Standalone single-file binary in `publish/win-x64/MangaGui.exe`
- Standalone setup installer in `publish/win-x64/MangaGui-Setup.exe`
- Portable distribution archive in `publish/manga-gui-windows.zip`

---

## 🔄 Auto-Updater
Manga GUI includes a built-in silent/assisted updater. When a new version is published to GitHub Pages, the application prompts you to update and seamlessly replaces files and restarts the app.

---

## 📄 License
This project is licensed under the GPL-3.0 License.
