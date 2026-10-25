# 🎬 MangaGui - Modern Desktop Manga & Anime Hub

**MangaGui** è un'interfaccia grafica desktop moderna, cross-platform e reattiva scritta in **C#** con **Avalonia UI** e pattern **MVVM**, progettata per offrire un'esperienza fluida e visivamente accattivante su **Windows** e **Linux** per guardare anime e leggere manga.

---

## ✨ Funzionalità

* 🎨 **Tema Scuro Nativo (Fluent Dark)**: Interfaccia raffinata con accenti viola e ciano, schede responsive e animazioni fluide.
* 🎬 **Anime Hub**: Navigazione e ricerca rapida nel catalogo di serie e film con badge audio dinamici (`ITA`, `SUB ITA`, `ITA / SUB`).
* 📖 **Manga Hub**: Sfoglia migliaia di volumi con filtri lingua e generi.
* ⚡ **Streaming Diretto a 1080p**: Supporto a fonti ad alta velocità (es. AnimeWorld) ed estrazione automatica dello stream.
* 🔌 **Supporto Multi-Fonte**: Filtro immediato per provider (AnimeWorld, One Piece Power, MangaDex, Mangapill).
* 🪟 **Multipiattaforma Reale**: Eseguibile sia su Linux sia su Windows con singolo eseguibile `.exe` standalone.

---

## 🚀 Avvio Rapido

### Su Linux (Sviluppo locale)
Assicurati di avere `.NET SDK` installato:
```bash
# Esegui l'applicazione
dotnet run
```

---

## 📦 Come Creare l'Eseguibile per Windows (`.exe` Standalone)

Puoi compilare direttamente da terminale un file `.exe` autonomo per Windows a 64-bit che **non richiede né Node.js né .NET installati** sul computer di destinazione:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/windows
```

L'eseguibile pronto per gli utenti Windows sarà salvato in:
`./publish/windows/MangaGui.exe`

---

## 🛠️ Tecnologie Utilizzate

* **Linguaggio**: C# (.NET 10 / .NET 8 LTS)
* **UI Framework**: [Avalonia UI](https://avaloniaui.net/) (v12)
* **Architettura**: MVVM con `CommunityToolkit.Mvvm`
* **Motore dati**: Integrazione asincrona con `manga-cli` via JSON machine-readable.

---

## 📜 Licenza
Rilasciato sotto licenza [MIT](LICENSE).
