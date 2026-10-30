using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MangaGui.Services;

public class UpdateCheckResult
{
    public bool CheckSucceeded { get; set; }
    public bool HasUpdate { get; set; }
    public string InstalledVersion { get; set; } = "1.0.0-2";
    public string RemoteVersion { get; set; } = "1.0.0-2";
    public string ReleaseNotes { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = "https://github.com/bhaki18/manga-gui.git";
    public string ArchPackageUrl { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class UpdateService
{
    public const string FallbackVersion = "1.0.0-2";
    public const string DefaultRepoUrl = "https://github.com/bhaki18/manga-gui.git";

    private static readonly string[] RemoteVersionJsonUrls =
    {
        "https://raw.githubusercontent.com/bhaki18/manga-cli/gh-pages/version.json",
        "https://bhaki18.github.io/manga-cli/version.json",
        "https://raw.githubusercontent.com/bhaki18/manga-gui/main/version.json",
        "https://raw.githubusercontent.com/bhaki18/manga-gui/master/version.json"
    };

    private static readonly string[] RemotePkgbuildUrls =
    {
        "https://raw.githubusercontent.com/bhaki18/manga-gui/main/PKGBUILD",
        "https://raw.githubusercontent.com/bhaki18/manga-gui/master/PKGBUILD"
    };

    public static string GetInstalledVersion()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "version.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "version.json"),
            Path.Combine(baseDir, "..", "..", "..", "version.json"),
            "/usr/lib/manga-gui/version.json"
        };

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    var raw = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
                    {
                        var v = vProp.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                    var pv = doc.RootElement.TryGetProperty("pkgver", out var pvProp) ? pvProp.GetString()?.Trim() : null;
                    var pr = doc.RootElement.TryGetProperty("pkgrel", out var prProp) ? prProp.GetString()?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(pv))
                    {
                        return !string.IsNullOrWhiteSpace(pr) ? $"{pv}-{pr}" : pv;
                    }
                }
            }
            catch { }
        }

        // Fallback: check local PKGBUILD if present
        var pkgbuildCandidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "PKGBUILD"),
            Path.Combine(baseDir, "..", "..", "..", "PKGBUILD")
        };

        foreach (var pb in pkgbuildCandidates)
        {
            try
            {
                if (File.Exists(pb))
                {
                    var parsed = ParsePkgbuildVersion(File.ReadAllText(pb));
                    if (!string.IsNullOrWhiteSpace(parsed)) return parsed;
                }
            }
            catch { }
        }

        return FallbackVersion;
    }

    public static string? ParsePkgbuildVersion(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var verMatch = Regex.Match(content, @"^\s*pkgver\s*=\s*([^\s#]+)", RegexOptions.Multiline);
        var relMatch = Regex.Match(content, @"^\s*pkgrel\s*=\s*([^\s#]+)", RegexOptions.Multiline);
        if (!verMatch.Success) return null;
        var ver = verMatch.Groups[1].Value.Trim().Trim('"', '\'');
        var rel = relMatch.Success ? relMatch.Groups[1].Value.Trim().Trim('"', '\'') : "1";
        return $"{ver}-{rel}";
    }

    /// <summary>
    /// Converts a version string like "1.0.0-2" or "v1.2.3" into a comparable integer array [major, minor, patch, pkgrel].
    /// </summary>
    public static int[] ParseVersionTuple(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new[] { 0, 0, 0, 0 };
        var clean = raw.Trim().TrimStart('v', 'V');
        var dashParts = clean.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        var mainPart = dashParts[0];
        var relPart = dashParts.Length > 1 ? dashParts[1] : "1";

        var dotParts = mainPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int major = dotParts.Length > 0 && int.TryParse(Regex.Match(dotParts[0], @"\d+").Value, out var m0) ? m0 : 0;
        int minor = dotParts.Length > 1 && int.TryParse(Regex.Match(dotParts[1], @"\d+").Value, out var m1) ? m1 : 0;
        int patch = dotParts.Length > 2 && int.TryParse(Regex.Match(dotParts[2], @"\d+").Value, out var m2) ? m2 : 0;
        int rel = int.TryParse(Regex.Match(relPart, @"\d+").Value, out var r0) ? r0 : 1;

        return new[] { major, minor, patch, rel };
    }

    /// <summary>
    /// Returns true ONLY when remoteVersion is strictly newer (greater) than installedVersion.
    /// If equal or older, returns false.
    /// </summary>
    public static bool IsRemoteNewer(string? installedVersion, string? remoteVersion)
    {
        var local = ParseVersionTuple(installedVersion);
        var remote = ParseVersionTuple(remoteVersion);

        for (int i = 0; i < Math.Max(local.Length, remote.Length); i++)
        {
            int l = i < local.Length ? local[i] : 0;
            int r = i < remote.Length ? remote[i] : 0;
            if (r > l) return true;
            if (r < l) return false;
        }
        return false;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        var installed = GetInstalledVersion();
        var result = new UpdateCheckResult
        {
            InstalledVersion = installed,
            RemoteVersion = installed,
            CheckSucceeded = false,
            HasUpdate = false
        };

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MangaGui-Updater/1.0");
        http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string? bestRemoteVersion = null;
        string bestNotes = "";
        string bestRepoUrl = DefaultRepoUrl;
        string bestArchPkgUrl = "";

        // 1. Check remote version.json endpoints
        foreach (var baseUrl in RemoteVersionJsonUrls)
        {
            try
            {
                var url = $"{baseUrl}?t={ts}";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? candidateVer = null;
                if (root.TryGetProperty("version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
                {
                    candidateVer = vProp.GetString()?.Trim();
                }
                else if (root.TryGetProperty("pkgver", out var pvProp) && pvProp.ValueKind == JsonValueKind.String)
                {
                    var pv = pvProp.GetString()?.Trim();
                    var pr = root.TryGetProperty("pkgrel", out var prProp) ? prProp.GetString()?.Trim() : "1";
                    if (!string.IsNullOrWhiteSpace(pv)) candidateVer = $"{pv}-{pr}";
                }

                if (!string.IsNullOrWhiteSpace(candidateVer))
                {
                    if (bestRemoteVersion == null || IsRemoteNewer(bestRemoteVersion, candidateVer))
                    {
                        bestRemoteVersion = candidateVer;
                        if (root.TryGetProperty("notes", out var nProp) && nProp.ValueKind == JsonValueKind.String)
                            bestNotes = nProp.GetString() ?? "";
                        if (root.TryGetProperty("repoUrl", out var rProp) && rProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rProp.GetString()))
                            bestRepoUrl = rProp.GetString()!;
                        if (root.TryGetProperty("archPackageUrl", out var aProp) && aProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(aProp.GetString()))
                            bestArchPkgUrl = aProp.GetString()!;
                    }
                }
            }
            catch { }
        }

        // 2. Also check remote PKGBUILD endpoints
        foreach (var pbUrl in RemotePkgbuildUrls)
        {
            try
            {
                var resp = await http.GetAsync($"{pbUrl}?t={ts}");
                if (!resp.IsSuccessStatusCode) continue;

                var text = await resp.Content.ReadAsStringAsync();
                var candidateVer = ParsePkgbuildVersion(text);
                if (!string.IsNullOrWhiteSpace(candidateVer))
                {
                    if (bestRemoteVersion == null || IsRemoteNewer(bestRemoteVersion, candidateVer))
                    {
                        bestRemoteVersion = candidateVer;
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(bestRemoteVersion))
        {
            result.CheckSucceeded = true;
            result.RemoteVersion = bestRemoteVersion;
            result.ReleaseNotes = bestNotes;
            result.RepoUrl = bestRepoUrl;
            result.ArchPackageUrl = !string.IsNullOrWhiteSpace(bestArchPkgUrl)
                ? bestArchPkgUrl
                : $"https://raw.githubusercontent.com/bhaki18/manga-cli/gh-pages/x86_64/manga-gui-bin-{bestRemoteVersion}-x86_64.pkg.tar.zst";
            result.HasUpdate = IsRemoteNewer(installed, bestRemoteVersion);
        }
        else
        {
            result.ErrorMessage = "Impossibile contattare il server degli aggiornamenti.";
        }

        return result;
    }

    public async Task<(bool success, string message)> PerformUpdateAsync(UpdateCheckResult updateInfo, Action<string>? onProgress = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return await PerformWindowsUpdateAsync(updateInfo, onProgress);
        }
        return await PerformLinuxUpdateAsync(updateInfo, onProgress);
    }

    private async Task<(bool success, string message)> PerformLinuxUpdateAsync(UpdateCheckResult updateInfo, Action<string>? onProgress)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "manga-gui-updater.sh");
            var baseDir = AppContext.BaseDirectory;
            var currentDir = Directory.GetCurrentDirectory();
            var isDevWorkspace = File.Exists(Path.Combine(currentDir, "MangaGui.csproj")) && Directory.Exists(Path.Combine(currentDir, ".git"));
            var isSystemInstall = baseDir.StartsWith("/usr/", StringComparison.Ordinal);

            var repoUrl = !string.IsNullOrWhiteSpace(updateInfo.RepoUrl) ? updateInfo.RepoUrl : DefaultRepoUrl;
            var archPkgUrl = !string.IsNullOrWhiteSpace(updateInfo.ArchPackageUrl)
                ? updateInfo.ArchPackageUrl
                : $"https://raw.githubusercontent.com/bhaki18/manga-cli/gh-pages/x86_64/manga-gui-bin-{updateInfo.RemoteVersion}-x86_64.pkg.tar.zst";

            var sb = new StringBuilder();
            sb.AppendLine("#!/usr/bin/env bash");
            sb.AppendLine("set -e");
            sb.AppendLine("");
            sb.AppendLine($"REPO_URL=\"{repoUrl}\"");
            sb.AppendLine($"REMOTE_VER=\"{updateInfo.RemoteVersion}\"");
            sb.AppendLine($"ARCH_PKG_URL=\"{archPkgUrl}\"");
            sb.AppendLine($"IS_DEV_WORKSPACE={(isDevWorkspace ? "true" : "false")}");
            sb.AppendLine($"DEV_DIR=\"{currentDir}\"");
            sb.AppendLine($"IS_SYSTEM_INSTALL={(isSystemInstall ? "true" : "false")}");
            sb.AppendLine("WORK_DIR=\"$HOME/.cache/manga-gui/updater-build\"");
            sb.AppendLine("");
            sb.AppendLine("run_privileged() {");
            sb.AppendLine("  if [ \"$(id -u)\" -eq 0 ]; then");
            sb.AppendLine("    \"$@\"");
            sb.AppendLine("  elif command -v pkexec >/dev/null 2>&1 && [ -n \"${DISPLAY:-}${WAYLAND_DISPLAY:-}\" ]; then");
            sb.AppendLine("    pkexec \"$@\"");
            sb.AppendLine("  elif command -v konsole >/dev/null 2>&1; then");
            sb.AppendLine("    konsole -e bash -c \"sudo $*\"");
            sb.AppendLine("  elif command -v gnome-terminal >/dev/null 2>&1; then");
            sb.AppendLine("    gnome-terminal --wait -- bash -c \"sudo $*\"");
            sb.AppendLine("  elif command -v xfce4-terminal >/dev/null 2>&1; then");
            sb.AppendLine("    xfce4-terminal --disable-server -x bash -c \"sudo $*\"");
            sb.AppendLine("  elif command -v kitty >/dev/null 2>&1; then");
            sb.AppendLine("    kitty bash -c \"sudo $*\"");
            sb.AppendLine("  elif command -v alacritty >/dev/null 2>&1; then");
            sb.AppendLine("    alacritty -e bash -c \"sudo $*\"");
            sb.AppendLine("  elif command -v xterm >/dev/null 2>&1; then");
            sb.AppendLine("    xterm -e \"sudo $*\"");
            sb.AppendLine("  else");
            sb.AppendLine("    sudo \"$@\"");
            sb.AppendLine("  fi");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("echo \"[STEP 1/4] Verifica dipendenze necessarie per l'aggiornamento (git, curl, nodejs, npm, mpv, dotnet)...\"");
            sb.AppendLine("MISSING_PKGS=()");
            sb.AppendLine("command -v git >/dev/null 2>&1 || MISSING_PKGS+=(\"git\")");
            sb.AppendLine("command -v curl >/dev/null 2>&1 || MISSING_PKGS+=(\"curl\")");
            sb.AppendLine("command -v node >/dev/null 2>&1 || MISSING_PKGS+=(\"nodejs\")");
            sb.AppendLine("command -v npm >/dev/null 2>&1 || MISSING_PKGS+=(\"npm\")");
            sb.AppendLine("command -v mpv >/dev/null 2>&1 || MISSING_PKGS+=(\"mpv\")");
            sb.AppendLine("command -v dotnet >/dev/null 2>&1 || MISSING_PKGS+=(\"dotnet-sdk\")");
            sb.AppendLine("");
            sb.AppendLine("if [ ${#MISSING_PKGS[@]} -gt 0 ]; then");
            sb.AppendLine("  echo \"[STEP 1/4] Installazione dipendenze mancanti: ${MISSING_PKGS[*]}...\"");
            sb.AppendLine("  if command -v pacman >/dev/null 2>&1; then");
            sb.AppendLine("    run_privileged pacman -S --needed --noconfirm \"${MISSING_PKGS[@]}\" || true");
            sb.AppendLine("  elif command -v apt-get >/dev/null 2>&1; then");
            sb.AppendLine("    run_privileged bash -c \"apt-get update && apt-get install -y git curl nodejs npm mpv\" || true");
            sb.AppendLine("  elif command -v dnf >/dev/null 2>&1; then");
            sb.AppendLine("    run_privileged dnf install -y git curl nodejs npm mpv dotnet-sdk-10.0 || true");
            sb.AppendLine("  fi");
            sb.AppendLine("else");
            sb.AppendLine("  echo \"[STEP 1/4] Tutte le dipendenze (git, curl, node, npm, mpv, dotnet) sono già installate.\"");
            sb.AppendLine("fi");
            sb.AppendLine("");
            sb.AppendLine("echo \"[STEP 2/4] Download dell'ultima versione ($REMOTE_VER) dal repository...\"");
            sb.AppendLine("if [ \"$IS_DEV_WORKSPACE\" = \"true\" ] && [ -w \"$DEV_DIR\" ]; then");
            sb.AppendLine("  BUILD_SRC=\"$DEV_DIR\"");
            sb.AppendLine("  cd \"$BUILD_SRC\"");
            sb.AppendLine("  git fetch origin || true");
            sb.AppendLine("  git pull --ff-only || true");
            sb.AppendLine("else");
            sb.AppendLine("  rm -rf \"$WORK_DIR\"");
            sb.AppendLine("  mkdir -p \"$(dirname \"$WORK_DIR\")\"");
            sb.AppendLine("  git clone --depth 1 \"$REPO_URL\" \"$WORK_DIR\"");
            sb.AppendLine("  BUILD_SRC=\"$WORK_DIR\"");
            sb.AppendLine("  cd \"$BUILD_SRC\"");
            sb.AppendLine("fi");
            sb.AppendLine("");
            sb.AppendLine("echo \"[STEP 3/4] Compilazione del progetto e delle dipendenze CLI...\"");
            sb.AppendLine("if [ -d \"./cli\" ] && command -v npm >/dev/null 2>&1; then");
            sb.AppendLine("  (cd ./cli && npm install --no-audit --no-fund)");
            sb.AppendLine("fi");
            sb.AppendLine("");
            sb.AppendLine("COMPILED_OK=false");
            sb.AppendLine("if command -v dotnet >/dev/null 2>&1; then");
            sb.AppendLine("  if dotnet publish MangaGui.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/linux-x64; then");
            sb.AppendLine("    mkdir -p ./publish/linux-x64/cli/src");
            sb.AppendLine("    cp -r ./cli/src/* ./publish/linux-x64/cli/src/");
            sb.AppendLine("    if [ -f ./version.json ]; then cp -f ./version.json ./publish/linux-x64/version.json; fi");
            sb.AppendLine("    chmod +x ./publish/linux-x64/MangaGui");
            sb.AppendLine("    COMPILED_OK=true");
            sb.AppendLine("  fi");
            sb.AppendLine("fi");
            sb.AppendLine("");
            sb.AppendLine("echo \"[STEP 4/4] Installazione dell'aggiornamento e riavvio...\"");
            sb.AppendLine("if command -v pacman >/dev/null 2>&1 && ( [ \"$IS_SYSTEM_INSTALL\" = \"true\" ] || pacman -Qi manga-gui-bin >/dev/null 2>&1 ); then");
            sb.AppendLine("  PKG_INSTALLED=false");
            sb.AppendLine("  if [ \"$COMPILED_OK\" = \"true\" ] && command -v makepkg >/dev/null 2>&1; then");
            sb.AppendLine("    makepkg -f && PKG_FILE=$(ls -t manga-gui-bin-*.pkg.tar.zst 2>/dev/null | grep -v \"\\-debug\\-\" | head -n 1 || true)");
            sb.AppendLine("    if [ -n \"$PKG_FILE\" ] && [ -f \"$PKG_FILE\" ]; then");
            sb.AppendLine("      run_privileged pacman -U --noconfirm \"$BUILD_SRC/$PKG_FILE\" && PKG_INSTALLED=true");
            sb.AppendLine("    fi");
            sb.AppendLine("  fi");
            sb.AppendLine("  if [ \"$PKG_INSTALLED\" = \"false\" ]; then");
            sb.AppendLine("    echo \"[STEP 4/4] Scaricamento pacchetto precompilato Arch da GitHub Pages...\"");
            sb.AppendLine("    TMP_PKG=\"/tmp/manga-gui-bin-update.pkg.tar.zst\"");
            sb.AppendLine("    curl -L --fail -o \"$TMP_PKG\" \"$ARCH_PKG_URL\"");
            sb.AppendLine("    run_privileged pacman -U --noconfirm \"$TMP_PKG\"");
            sb.AppendLine("  fi");
            sb.AppendLine("  echo \"[COMPLETATO] Riavvio di Manga GUI aggiornato...\"");
            sb.AppendLine("  nohup /usr/bin/manga-gui >/dev/null 2>&1 &");
            sb.AppendLine("elif [ \"$COMPILED_OK\" = \"true\" ]; then");
            sb.AppendLine("  echo \"[COMPLETATO] Riavvio di Manga GUI dalla build aggiornata...\"");
            sb.AppendLine("  nohup \"$BUILD_SRC/publish/linux-x64/MangaGui\" >/dev/null 2>&1 &");
            sb.AppendLine("else");
            sb.AppendLine("  echo \"Errore: impossibile completare la compilazione o l'installazione.\"");
            sb.AppendLine("  exit 1");
            sb.AppendLine("fi");

            await File.WriteAllTextAsync(scriptPath, sb.ToString());
            try
            {
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }
            catch { }

            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"\"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            MangaCliService.PassDisplayEnv(psi);

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    onProgress?.Invoke(e.Data.Trim());
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    onProgress?.Invoke(e.Data.Trim());
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                onProgress?.Invoke("✔ Aggiornamento completato! Riavvio dell'applicazione in corso...");
                await Task.Delay(900);
                Environment.Exit(0);
                return (true, "Aggiornamento completato con successo.");
            }

            return (false, $"L'aggiornamento è terminato con codice {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"Errore durante l'aggiornamento: {ex.Message}");
        }
    }

    private async Task<(bool success, string message)> PerformWindowsUpdateAsync(UpdateCheckResult updateInfo, Action<string>? onProgress)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "manga-gui-win-update");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);

            var buildBatPath = Path.Combine(tempDir, "build-update.bat");
            var applyBatPath = Path.Combine(tempDir, "apply-update.bat");
            var repoUrl = !string.IsNullOrWhiteSpace(updateInfo.RepoUrl) ? updateInfo.RepoUrl : DefaultRepoUrl;
            var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var currentPid = Environment.ProcessId;

            // 1. Script di download dipendenze + clone + compilazione (eseguito con progresso in tempo reale nella GUI)
            var buildBat = new StringBuilder();
            buildBat.AppendLine("@echo off");
            buildBat.AppendLine("setlocal EnableDelayedExpansion");
            buildBat.AppendLine("set \"PATH=%PATH%;C:\\Program Files\\Git\\cmd;C:\\Program Files\\nodejs;C:\\Program Files\\dotnet\"");
            buildBat.AppendLine("echo [STEP 1/4] Verifica e installazione dipendenze Windows (Git, Node.js, .NET SDK, VC++ Redist)...");
            buildBat.AppendLine("where git >nul 2>nul || (echo Installazione Git... && winget install --id Git.Git -e --silent --accept-package-agreements --accept-source-agreements)");
            buildBat.AppendLine("where node >nul 2>nul || (echo Installazione Node.js... && winget install --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements)");
            buildBat.AppendLine("where dotnet >nul 2>nul || (echo Installazione .NET 10 SDK... && winget install --id Microsoft.DotNet.SDK.10 -e --silent --accept-package-agreements --accept-source-agreements)");
            buildBat.AppendLine("set \"PATH=%PATH%;C:\\Program Files\\Git\\cmd;C:\\Program Files\\nodejs;C:\\Program Files\\dotnet\"");
            buildBat.AppendLine($"echo [STEP 2/4] Scaricamento ultima versione ({updateInfo.RemoteVersion}) da {repoUrl}...");
            buildBat.AppendLine($"git clone --depth 1 \"{repoUrl}\" \"{tempDir}\\src\"");
            buildBat.AppendLine("if errorlevel 1 exit /b 1");
            buildBat.AppendLine("echo [STEP 3/4] Installazione dipendenze CLI (npm) e compilazione MangaGui.exe...");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\cli\\package.json\" (cd /d \"{tempDir}\\src\\cli\" && call npm install --no-audit --no-fund)");
            buildBat.AppendLine($"cd /d \"{tempDir}\\src\"");
            buildBat.AppendLine($"dotnet publish MangaGui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o \"{tempDir}\\out\"");
            buildBat.AppendLine("if errorlevel 1 exit /b 1");
            buildBat.AppendLine($"xcopy /E /I /Y \"{tempDir}\\src\\cli\\src\" \"{tempDir}\\out\\cli\\src\" >nul");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\cli\\node_modules\" xcopy /E /I /Y \"{tempDir}\\src\\cli\\node_modules\" \"{tempDir}\\out\\cli\\node_modules\" >nul");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\version.json\" copy /Y \"{tempDir}\\src\\version.json\" \"{tempDir}\\out\\version.json\" >nul");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\Assets\\manga-gui.ico\" copy /Y \"{tempDir}\\src\\Assets\\manga-gui.ico\" \"{tempDir}\\out\\manga-gui.ico\" >nul");
            buildBat.AppendLine("echo [STEP 4/4] Compilazione Windows completata! Preparazione riavvio...");
            buildBat.AppendLine("exit /b 0");

            await File.WriteAllTextAsync(buildBatPath, buildBat.ToString());

            // 2. Script di sostituzione file (attende la chiusura di MangaGui.exe perché Windows blocca i file .exe in esecuzione) e riavvio
            var applyBat = new StringBuilder();
            applyBat.AppendLine("@echo off");
            applyBat.AppendLine("timeout /t 2 /nobreak >nul");
            applyBat.AppendLine($"taskkill /PID {currentPid} /F >nul 2>nul");
            applyBat.AppendLine($"xcopy /E /I /Y \"{tempDir}\\out\\*\" \"{installDir}\\\" >nul");
            applyBat.AppendLine($"start \"\" \"{installDir}\\MangaGui.exe\"");
            applyBat.AppendLine("exit /b 0");

            await File.WriteAllTextAsync(applyBatPath, applyBat.ToString());

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{buildBatPath}\"\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    onProgress?.Invoke(e.Data.Trim());
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    onProgress?.Invoke(e.Data.Trim());
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                onProgress?.Invoke("✔ Aggiornamento Windows pronto! Riavvio in corso...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{applyBatPath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                await Task.Delay(800);
                Environment.Exit(0);
                return (true, "Aggiornamento Windows completato.");
            }

            return (false, $"La compilazione dell'aggiornamento Windows è terminata con codice {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"Errore durante l'aggiornamento Windows: {ex.Message}");
        }
    }
}

