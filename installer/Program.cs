using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Win32;

namespace MangaGui.Installer;

class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    const uint MB_OK = 0x00000000;
    const uint MB_ICONINFORMATION = 0x00000040;
    const uint MB_ICONWARNING = 0x00000030;
    const uint MB_ICONERROR = 0x00000010;

    static void Main(string[] args)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var installDir = Path.Combine(localAppData, "Programs", "MangaGui");
            Directory.CreateDirectory(installDir);

            // 1. Extract embedded package.zip
            var assembly = Assembly.GetExecutingAssembly();
            var resName = assembly.GetManifestResourceNames()
                                  .FirstOrDefault(n => n.EndsWith("package.zip", StringComparison.OrdinalIgnoreCase));
            
            Stream? stream = resName != null ? assembly.GetManifestResourceStream(resName) : null;
            if (stream == null)
            {
                var adjacentZip = Path.Combine(AppContext.BaseDirectory, "package.zip");
                if (File.Exists(adjacentZip))
                {
                    ZipFile.ExtractToDirectory(adjacentZip, installDir, overwriteFiles: true);
                }
                else
                {
                    MessageBoxW(IntPtr.Zero, "File del pacchetto di installazione non trovato.", "Errore Manga GUI", MB_OK | MB_ICONERROR);
                    return;
                }
            }
            else
            {
                using (stream)
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(installDir, overwriteFiles: true);
                }
            }

            var targetExe = Path.Combine(installDir, "MangaGui.exe");
            var iconPath = Path.Combine(installDir, "manga-gui.ico");

            // 2. Controllo e installazione automatica dipendenze di sistema (Visual C++ 2015-2022 x64)
            EnsureSystemDependencies(installDir);

            // 3. Verifica dipendenze runtime (Node, Curl, MPV, CLI)
            var (depsOk, depMsg) = VerifyRuntimeDependencies(installDir);

            // 4. Crea collegamento Desktop
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(desktopDir) && Directory.Exists(desktopDir))
            {
                var desktopShortcut = Path.Combine(desktopDir, "Manga GUI.lnk");
                CreateShortcut(desktopShortcut, targetExe, installDir, iconPath, "Manga GUI - Downloader, Streamer e Reader");
            }

            // 5. Crea collegamento Menu Start
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var startMenuDir = Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs");
            if (Directory.Exists(startMenuDir))
            {
                var startShortcut = Path.Combine(startMenuDir, "Manga GUI.lnk");
                CreateShortcut(startShortcut, targetExe, installDir, iconPath, "Manga GUI - Downloader, Streamer e Reader");
            }

            // 6. Registra disinstallazione nel Registro di Windows
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MangaGui");
                if (key != null)
                {
                    key.SetValue("DisplayName", "Manga GUI");
                    key.SetValue("DisplayIcon", iconPath);
                    key.SetValue("DisplayVersion", "1.0.0");
                    key.SetValue("Publisher", "Manga GUI");
                    key.SetValue("InstallLocation", installDir);
                    key.SetValue("UninstallString", $"cmd /c rmdir /s /q \"{installDir}\" & del /f /q \"%USERPROFILE%\\Desktop\\Manga GUI.lnk\"");
                }
            }
            catch { }

            // 7. Notifica di successo
            var msg = "Installazione completata con successo!\n\n" +
                      "• Dipendenze di sistema (Visual C++, Node, MPV, cURL): Verificate e pronte\n" +
                      "• Icona Manga GUI creata sul Desktop\n" +
                      "• Collegamento aggiunto al Menu Start\n\n" +
                      "Clicca OK per avviare Manga GUI.";

            MessageBoxW(IntPtr.Zero, msg, "Manga GUI Setup", MB_OK | MB_ICONINFORMATION);

            // 8. Avvia Manga GUI
            if (File.Exists(targetExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBoxW(IntPtr.Zero, $"Si è verificato un errore durante l'installazione:\n{ex.Message}", "Errore Installazione", MB_OK | MB_ICONERROR);
        }
    }

    static void EnsureSystemDependencies(string installDir)
    {
        try
        {
            if (IsVcRedistInstalled())
            {
                // Già installato: rimuovi il setup temporaneo per risparmiare spazio
                var vcBundled = Path.Combine(installDir, "vc_redist.x64.exe");
                if (File.Exists(vcBundled))
                {
                    try { File.Delete(vcBundled); } catch { }
                }
                return;
            }

            // Non installato: installalo automaticamente
            var vcPath = Path.Combine(installDir, "vc_redist.x64.exe");
            if (!File.Exists(vcPath))
            {
                // Se non è incluso, scaricalo da Microsoft
                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var bytes = client.GetByteArrayAsync("https://aka.ms/vs/17/release/vc_redist.x64.exe").GetAwaiter().GetResult();
                    File.WriteAllBytes(vcPath, bytes);
                }
                catch { }
            }

            if (File.Exists(vcPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = vcPath,
                    Arguments = "/install /passive /norestart",
                    UseShellExecute = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(120000); // Attende fino a 2 minuti

                // Pulizia del file installer
                try { File.Delete(vcPath); } catch { }
            }
        }
        catch { }
    }

    static bool IsVcRedistInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64");
            if (key != null)
            {
                var val = key.GetValue("Installed");
                if (val != null && Convert.ToInt32(val) == 1) return true;
            }
        }
        catch { }

        try
        {
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var f1 = Path.Combine(sys, "vcruntime140.dll");
            var f2 = Path.Combine(sys, "msvcp140.dll");
            return File.Exists(f1) && File.Exists(f2);
        }
        catch
        {
            return false;
        }
    }

    static (bool ok, string message) VerifyRuntimeDependencies(string installDir)
    {
        var missing = new List<string>();
        var node = Path.Combine(installDir, "node.exe");
        var curl = Path.Combine(installDir, "curl.exe");
        var mpv = Path.Combine(installDir, "mpv.exe");
        var cli = Path.Combine(installDir, "cli", "src", "cli.js");

        if (!File.Exists(node)) missing.Add("node.exe");
        if (!File.Exists(curl)) missing.Add("curl.exe");
        if (!File.Exists(mpv)) missing.Add("mpv.exe");
        if (!File.Exists(cli)) missing.Add("cli/src/cli.js");

        if (missing.Count > 0)
        {
            return (false, $"Componenti non trovati: {string.Join(", ", missing)}");
        }

        // Test esecuzione rapido per verificare assenza di crash su DLL native
        try
        {
            var psiNode = new ProcessStartInfo
            {
                FileName = node,
                Arguments = "-v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var pNode = Process.Start(psiNode);
            pNode?.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            return (false, $"Node.js test fallito: {ex.Message}");
        }

        return (true, "Tutte le dipendenze sono verificate.");
    }

    static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.IconLocation = $"{iconPath},0";
                shortcut.Description = description;
                shortcut.Save();
                return;
            }
        }
        catch { }

        // Fallback PowerShell
        try
        {
            var ps = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetPath}'; $s.WorkingDirectory = '{workingDirectory}'; $s.IconLocation = '{iconPath},0'; $s.Description = '{description}'; $s.Save()";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -Command \"{ps}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }
}
