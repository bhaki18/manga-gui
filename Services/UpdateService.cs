using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    public string WindowsZipUrl { get; set; } = string.Empty;
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

    public static string GetInstalledVersion()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "version.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "version.json"),
            Path.Combine(baseDir, "..", "..", "..", "version.json")
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

        return FallbackVersion;
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
        string bestWindowsZipUrl = "";

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
                        if (root.TryGetProperty("windowsZipUrl", out var wProp) && wProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(wProp.GetString()))
                            bestWindowsZipUrl = wProp.GetString()!;
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
            result.WindowsZipUrl = !string.IsNullOrWhiteSpace(bestWindowsZipUrl)
                ? bestWindowsZipUrl
                : "https://bhaki18.github.io/manga-cli/windows/manga-gui-windows.zip";
            result.HasUpdate = IsRemoteNewer(installed, bestRemoteVersion);
        }
        else
        {
            result.ErrorMessage = "Unable to contact update server.";
        }

        return result;
    }

    public async Task<(bool success, string message)> PerformUpdateAsync(UpdateCheckResult updateInfo, Action<string>? onProgress = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return await PerformWindowsUpdateAsync(updateInfo, onProgress);
        }
        return (false, "Manga GUI for Windows does not support updating Linux installations. Please use manga-gui-linux.");
    }

    private static void DeleteDirectorySafe(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                try { file.Attributes = FileAttributes.Normal; } catch { }
            }
            dir.Delete(true);
        }
        catch { }
    }

    private async Task<(bool success, string message)> PerformWindowsUpdateAsync(UpdateCheckResult updateInfo, Action<string>? onProgress)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "manga-gui-win-update");
            DeleteDirectorySafe(tempDir);
            Directory.CreateDirectory(tempDir);

            var applyBatPath = Path.Combine(tempDir, "apply-update.bat");
            var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var currentPid = Environment.ProcessId;

            // Tentativo 1: Download diretto del pacchetto ZIP precompilato (veloce, affidabile, zero dipendenze)
            var zipUrls = new[]
            {
                !string.IsNullOrWhiteSpace(updateInfo.WindowsZipUrl) ? updateInfo.WindowsZipUrl : "https://bhaki18.github.io/manga-cli/windows/manga-gui-windows.zip",
                "https://raw.githubusercontent.com/bhaki18/manga-cli/gh-pages/windows/manga-gui-windows.zip",
                "https://bhaki18.github.io/manga-cli/windows/manga-gui-win-x64.zip"
            };

            var zipPath = Path.Combine(tempDir, "update.zip");
            var outDir = Path.Combine(tempDir, "out");
            bool zipDownloaded = false;

            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MangaGui-Updater");
                foreach (var url in zipUrls)
                {
                    try
                    {
                        onProgress?.Invoke($"[1/3] Downloading prebuilt package v{updateInfo.RemoteVersion}...");
                        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        if (res.IsSuccessStatusCode)
                        {
                            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await res.Content.CopyToAsync(fs);
                            }
                            zipDownloaded = true;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (zipDownloaded && File.Exists(zipPath))
            {
                onProgress?.Invoke("[2/3] Extracting updated files...");
                Directory.CreateDirectory(outDir);
                ZipFile.ExtractToDirectory(zipPath, outDir, overwriteFiles: true);

                var subFolder = Path.Combine(outDir, "manga-gui-win-x64");
                var effectiveOutDir = Directory.Exists(subFolder) ? subFolder : outDir;

                // Ensure version.json contains the updated version
                var verFile = Path.Combine(effectiveOutDir, "version.json");
                if (!File.Exists(verFile))
                {
                    var verJson = $"{{\"version\": \"{updateInfo.RemoteVersion}\", \"releaseDate\": \"{DateTime.UtcNow:yyyy-MM-dd}\"}}";
                    await File.WriteAllTextAsync(verFile, verJson);
                }

                onProgress?.Invoke("[3/3] Applying update and restarting Manga GUI...");

                var applyBat = new StringBuilder();
                applyBat.AppendLine("@echo off");
                applyBat.AppendLine("timeout /t 2 /nobreak >nul");
                applyBat.AppendLine($"taskkill /PID {currentPid} /F >nul 2>nul");
                applyBat.AppendLine($"xcopy /E /I /Y \"{effectiveOutDir}\\*\" \"{installDir}\\\" >nul");
                applyBat.AppendLine($"powershell -NoProfile -ExecutionPolicy Bypass -Command \"Get-ChildItem -Path '{installDir}' -Recurse | Unblock-File\" >nul 2>nul");
                applyBat.AppendLine($"start \"\" \"{installDir}\\MangaGui.exe\"");
                applyBat.AppendLine("exit /b 0");

                await File.WriteAllTextAsync(applyBatPath, applyBat.ToString());

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{applyBatPath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                await Task.Delay(800);
                Environment.Exit(0);
                return (true, "Windows update completed.");
            }

            // Fallback: Build from source via Git if ZIP download was unsuccessful
            var repoUrl = !string.IsNullOrWhiteSpace(updateInfo.RepoUrl) ? updateInfo.RepoUrl : DefaultRepoUrl;
            var buildBatPath = Path.Combine(tempDir, "build-update.bat");

            var buildBat = new StringBuilder();
            buildBat.AppendLine("@echo off");
            buildBat.AppendLine("setlocal EnableDelayedExpansion");
            buildBat.AppendLine("set \"PATH=%PATH%;C:\\Program Files\\Git\\cmd;C:\\Program Files\\nodejs;C:\\Program Files\\dotnet\"");
            buildBat.AppendLine("echo [STEP 1/4] Verifying and installing Windows dependencies (Git, Node.js, .NET SDK, VC++ Redist)...");
            buildBat.AppendLine("where git >nul 2>nul || (echo Installing Git... && winget install --id Git.Git -e --silent --accept-package-agreements --accept-source-agreements)");
            buildBat.AppendLine("where node >nul 2>nul || (echo Installing Node.js... && winget install --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements)");
            buildBat.AppendLine("where dotnet >nul 2>nul || (echo Installing .NET 10 SDK... && winget install --id Microsoft.DotNet.SDK.10 -e --silent --accept-package-agreements --accept-source-agreements)");
            buildBat.AppendLine("set \"PATH=%PATH%;C:\\Program Files\\Git\\cmd;C:\\Program Files\\nodejs;C:\\Program Files\\dotnet\"");
            buildBat.AppendLine($"echo [STEP 2/4] Downloading latest version ({updateInfo.RemoteVersion}) from {repoUrl}...");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\.git\" (");
            buildBat.AppendLine($"  cd /d \"{tempDir}\\src\" && git fetch --depth 1 origin main && git reset --hard origin/main");
            buildBat.AppendLine(") else (");
            buildBat.AppendLine($"  if exist \"{tempDir}\\src\" rmdir /s /q \"{tempDir}\\src\" 2>nul");
            buildBat.AppendLine($"  git clone --depth 1 \"{repoUrl}\" \"{tempDir}\\src\"");
            buildBat.AppendLine(")");
            buildBat.AppendLine("if errorlevel 1 exit /b 1");
            buildBat.AppendLine("echo [STEP 3/4] Installing CLI dependencies (npm) and building MangaGui.exe...");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\cli\\package.json\" (cd /d \"{tempDir}\\src\\cli\" && call npm install --no-audit --no-fund)");
            buildBat.AppendLine($"cd /d \"{tempDir}\\src\"");
            buildBat.AppendLine($"dotnet publish MangaGui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o \"{tempDir}\\out\"");
            buildBat.AppendLine("if errorlevel 1 exit /b 1");
            buildBat.AppendLine($"xcopy /E /I /Y \"{tempDir}\\src\\cli\\src\" \"{tempDir}\\out\\cli\\src\" >nul");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\cli\\node_modules\" xcopy /E /I /Y \"{tempDir}\\src\\cli\\node_modules\" \"{tempDir}\\out\\cli\\node_modules\" >nul");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\version.json\" copy /Y \"{tempDir}\\src\\version.json\" \"{tempDir}\\out\\version.json\" >nul");
            buildBat.AppendLine($"if exist \"{tempDir}\\src\\Assets\\manga-gui.ico\" copy /Y \"{tempDir}\\src\\Assets\\manga-gui.ico\" \"{tempDir}\\out\\manga-gui.ico\" >nul");
            buildBat.AppendLine("echo [STEP 4/4] Windows build completed! Preparing restart...");
            buildBat.AppendLine("exit /b 0");

            await File.WriteAllTextAsync(buildBatPath, buildBat.ToString());

            var applyBatFallback = new StringBuilder();
            applyBatFallback.AppendLine("@echo off");
            applyBatFallback.AppendLine("timeout /t 2 /nobreak >nul");
            applyBatFallback.AppendLine($"taskkill /PID {currentPid} /F >nul 2>nul");
            applyBatFallback.AppendLine($"xcopy /E /I /Y \"{tempDir}\\out\\*\" \"{installDir}\\\" >nul");
            applyBatFallback.AppendLine($"powershell -NoProfile -ExecutionPolicy Bypass -Command \"Get-ChildItem -Path '{installDir}' -Recurse | Unblock-File\" >nul 2>nul");
            applyBatFallback.AppendLine($"start \"\" \"{installDir}\\MangaGui.exe\"");
            applyBatFallback.AppendLine("exit /b 0");

            await File.WriteAllTextAsync(applyBatPath, applyBatFallback.ToString());

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
                if (!string.IsNullOrWhiteSpace(e.Data)) onProgress?.Invoke(e.Data.Trim());
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) onProgress?.Invoke(e.Data.Trim());
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                onProgress?.Invoke("✔ Windows update ready! Restarting...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{applyBatPath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                await Task.Delay(800);
                Environment.Exit(0);
                return (true, "Windows update completed.");
            }

            return (false, $"Windows update finished with exit code {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"Error during Windows update: {ex.Message}");
        }
    }
}
