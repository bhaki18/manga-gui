# 🚀 Developer Guide: Releasing Manga GUI Updates for Windows

This guide describes how the integrated Auto-Updater works in **Manga GUI** for Windows and the steps to publish updates.

---

## 🧠 1. How the Auto-Updater Works

On application startup (or when clicking **`🔄 Check Updates`** in the bottom status bar), `UpdateService`:

1. **Reads Installed Version**:
   - Reads local version from `version.json`.
2. **Checks Published Version Online**:
   - Queries `https://raw.githubusercontent.com/bhaki18/manga-cli/gh-pages/version.json`.
3. **Compares Versions (`IsRemoteNewer`)**:
   - If online version is strictly higher (`>`), prompts the user with release notes.
4. **Applies Update**:
   - Downloads `manga-gui-windows.zip`, unpacks it, launches `apply-update.bat` to replace files, and restarts `MangaGui.exe`.

---

## ⚡ 2. Quick Release Procedure

### Step 1: Commit and push changes
```bash
git add .
git commit -m "feat/fix: description of changes"
git push origin main
```

### Step 2: Build and publish to GitHub Pages
```bash
./publish-gh-pages.sh --version 1.2.8 -n "Bug fixes and improvements"
```
Or simply build without publishing:
```bash
./build-packages.sh
```

---

> 🐧 For Linux package releases, refer to [`manga-gui-linux`](https://github.com/bhaki18/manga-gui-linux).
