using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MangaGui.Models;

namespace MangaGui.Services;

public class MangaCliService
{
    private readonly string _cliScriptPath;

    public MangaCliService()
    {
        var localCli = "/home/adp/Desktop/manga-cli/src/cli.js";
        _cliScriptPath = File.Exists(localCli) ? localCli : "manga-cli";
    }

    private async Task<string> RunCommandAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_cliScriptPath.EndsWith(".js") && File.Exists(_cliScriptPath))
        {
            psi.FileName = "node";
            psi.Arguments = $"\"{_cliScriptPath}\" {arguments}";
        }
        else
        {
            psi.FileName = "manga-cli";
            psi.Arguments = arguments;
        }

        PassDisplayEnv(psi);

        using var process = Process.Start(psi) 
            ?? throw new InvalidOperationException("Failed to start manga-cli process");

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Trim();
    }

    public async Task<List<MediaItem>> GetAnimeCatalogAsync(string? source = null)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" ? $"--source {source}" : "";
            var json = await RunCommandAsync($"catalog anime {srcArg} --json");
            var res = JsonSerializer.Deserialize<CatalogResponse>(json);
            return res?.Anime ?? new List<MediaItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching anime catalog: {ex.Message}");
            return new List<MediaItem>();
        }
    }

        public async Task<List<MediaItem>> GetHistoryAsync()
    {
        try
        {
            var json = await RunCommandAsync("--history --json");
            return JsonSerializer.Deserialize<List<MediaItem>>(json) ?? new List<MediaItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching history: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<List<MediaItem>> GetMangaCatalogAsync(string? source = null)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" ? $"--source {source}" : "";
            var json = await RunCommandAsync($"catalog manga {srcArg} --json");
            var res = JsonSerializer.Deserialize<CatalogResponse>(json);
            return res?.Manga ?? new List<MediaItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching manga catalog: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<List<MediaItem>> SearchAnimeAsync(string query, string? source = null)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" ? $"--source {source}" : "";
            var json = await RunCommandAsync($"\"{query}\" -a {srcArg} --json");
            return JsonSerializer.Deserialize<List<MediaItem>>(json) ?? new List<MediaItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching anime: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<List<MediaItem>> SearchMangaAsync(string query, string? source = null, string? lang = null)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" ? $"--source {source}" : "";
            var langArg = !string.IsNullOrWhiteSpace(lang) ? $"-l {lang}" : "";
            var json = await RunCommandAsync($"\"{query}\" -m {srcArg} {langArg} --json");
            return JsonSerializer.Deserialize<List<MediaItem>>(json) ?? new List<MediaItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching manga: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<bool> PlayStreamAsync(string title, int episode, string? source = null)
    {
        var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" ? $"--source {source}" : "";
        try
        {
            // Try getting the direct stream URL first for instant MPV playback
            var streamUrl = await RunCommandAsync($"\"{title}\" -a -e {episode} {srcArg} --get-url");
            var cleanUrl = streamUrl.Trim();
            if (!string.IsNullOrWhiteSpace(cleanUrl) && (cleanUrl.StartsWith("http://") || cleanUrl.StartsWith("https://")))
            {
                var mpvPsi = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = $"\"{cleanUrl}\" --title=\"{title} - Episodio {episode}\" --force-window=immediate --save-position-on-quit",
                    UseShellExecute = false
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Direct MPV extraction error: {ex.Message}");
        }

        // Fallback: Launch manga-cli player detached
        LaunchDetached($"\"{title}\" -a -e {episode} {srcArg}");
        return true;
    }

    public Task<bool> ReadMangaAsync(string title, int chapter, string? source = null)
    {
        var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" ? $"--source {source}" : "";
        LaunchDetached($"\"{title}\" -m -C {chapter} {srcArg}");
        return Task.FromResult(true);
    }

    private void LaunchDetached(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_cliScriptPath.EndsWith(".js") && File.Exists(_cliScriptPath))
        {
            psi.FileName = "node";
            psi.Arguments = $"\"{_cliScriptPath}\" {arguments}";
        }
        else
        {
            psi.FileName = "manga-cli";
            psi.Arguments = arguments;
        }

        PassDisplayEnv(psi);
        Process.Start(psi);
    }

    private void PassDisplayEnv(ProcessStartInfo psi)
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display)) psi.Environment["DISPLAY"] = display;

        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!string.IsNullOrEmpty(wayland)) psi.Environment["WAYLAND_DISPLAY"] = wayland;

        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg)) psi.Environment["XDG_RUNTIME_DIR"] = xdg;
    }
}
