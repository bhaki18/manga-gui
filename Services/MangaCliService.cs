using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MangaGui.Models;

namespace MangaGui.Services;

public class StreamInfoResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("streamUrl")]
    public string? StreamUrl { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("episode")]
    public string? Episode { get; set; }

    [JsonPropertyName("referer")]
    public string? Referer { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class ChapterInfoResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("pages")]
    public List<string> Pages { get; set; } = new();

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("chapter")]
    public string? Chapter { get; set; }

    [JsonPropertyName("referer")]
    public string? Referer { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class MediaDetailsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; } = 1;

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

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

    public async Task<int> GetMediaDetailsAsync(MediaItem item)
    {
        try
        {
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) ? $"--url \"{item.Url}\"" : "";
            var srcArg = !string.IsNullOrWhiteSpace(item.Source) && item.Source != "all" ? $"--source {item.Source}" : "";
            var json = await RunCommandAsync($"media-details {urlArg} {srcArg} --json");
            var details = JsonSerializer.Deserialize<MediaDetailsResponse>(json);
            return details != null && details.Total > 0 ? details.Total : 1;
        }
        catch
        {
            return 1;
        }
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
            var langArg = !string.IsNullOrWhiteSpace(lang) ? $"-l {lang}" : "-l it";
            var json = await RunCommandAsync($"\"{query}\" -m {srcArg} {langArg} --json");
            return JsonSerializer.Deserialize<List<MediaItem>>(json) ?? new List<MediaItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching manga: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<(bool success, string message)> PlayStreamAsync(MediaItem item, int episode)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(item.Source) && item.Source != "all" ? $"--source {item.Source}" : "";
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) ? $"--url \"{item.Url}\"" : "";

            var json = await RunCommandAsync($"stream-info {urlArg} --episode {episode} {srcArg} --json");
            var info = JsonSerializer.Deserialize<StreamInfoResponse>(json);

            if (info != null && info.Success && !string.IsNullOrWhiteSpace(info.StreamUrl))
            {
                var refHeader = !string.IsNullOrWhiteSpace(info.Referer) ? $"--referrer=\"{info.Referer}\"" : "";
                var mpvPsi = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = $"\"{info.StreamUrl}\" --title=\"{item.Title} - Episodio {episode}\" --force-window=immediate {refHeader}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);
                return (true, $"In riproduzione: {item.Title} (Episodio {episode})");
            }

            return (false, info?.Error ?? "Impossibile estrarre il flusso video per questo episodio.");
        }
        catch (Exception ex)
        {
            return (false, $"Errore riproduzione: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> ReadMangaAsync(MediaItem item, int chapter)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(item.Source) && item.Source != "all" ? $"--source {item.Source}" : "";
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) ? $"--url \"{item.Url}\"" : "";

            var json = await RunCommandAsync($"chapter-info {urlArg} --chapter {chapter} {srcArg} --json");
            var info = JsonSerializer.Deserialize<ChapterInfoResponse>(json);

            if (info != null && info.Success && info.Pages != null && info.Pages.Count > 0)
            {
                var pagesArgs = string.Join(" ", info.Pages.Select(p => $"\"{p}\""));
                var mpvPsi = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = $"--title=\"{item.Title} - Capitolo {chapter}\" --image-display-duration=inf --force-window=immediate {pagesArgs}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);
                return (true, $"Aperto lettore: {item.Title} (Capitolo {chapter} - {info.Pages.Count} pagine)");
            }

            return (false, info?.Error ?? "Nessuna pagina trovata per questo capitolo.");
        }
        catch (Exception ex)
        {
            return (false, $"Errore lettura: {ex.Message}");
        }
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
