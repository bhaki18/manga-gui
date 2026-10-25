using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MangaGui.Models;

namespace MangaGui.Services;

public class MangaCliService
{
    private readonly string _cliScriptPath;
    private static readonly string _historyFile;

    static MangaCliService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _historyFile = Path.Combine(home, ".local", "state", "manga-cli", "history.json");
    }

    public MangaCliService()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "cli", "src", "cli.js"),
            Path.Combine(baseDir, "..", "..", "..", "cli", "src", "cli.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "cli", "src", "cli.js"),
            "/home/adp/Desktop/manga-gui/cli/src/cli.js"
        };

        _cliScriptPath = candidates.FirstOrDefault(File.Exists) ?? "manga-cli";
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
            var cliDir = Path.GetDirectoryName(Path.GetDirectoryName(_cliScriptPath));
            if (!string.IsNullOrEmpty(cliDir) && Directory.Exists(cliDir))
            {
                psi.WorkingDirectory = cliDir;
            }
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
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" && source != "Tutte le fonti" && source != "All sources" 
                ? $"-s \"{source}\"" : "";
            var output = await RunCommandAsync($"--catalog-anime {srcArg} --list");
            var items = ParseCatalogOutput(output, "anime");

            EnrichCoversAsync(items, "anime");

            return items;
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
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" && source != "Tutte le fonti" && source != "All sources" 
                ? $"-s \"{source}\"" : "";
            var output = await RunCommandAsync($"--catalog-manga {srcArg} --list");
            var items = ParseCatalogOutput(output, "manga");

            EnrichCoversAsync(items, "manga");

            return items;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching manga catalog: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    
    private static string? GetSegmentedQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var clean = query.Trim().ToLower();
        if (clean.Contains(' ') || clean.Length < 4) return null;

        var dict = new HashSet<string>
        {
            "fire", "force", "one", "piece", "death", "note", "black", "clover", "demon", "slayer",
            "fairy", "tail", "solo", "leveling", "tokyo", "ghoul", "chain", "saw", "chainsaw", "man",
            "attack", "on", "titan", "sword", "art", "online", "my", "hero", "academia", "jujutsu", "kaisen",
            "dr", "stone", "full", "metal", "fullmetal", "alchemist", "hunter", "dandadan", "dan", "da",
            "spy", "family", "punch", "dragon", "ball", "bleach", "naruto", "boruto", "berserk", "blue",
            "exorcist", "ao", "no"
        };

        for (int i = 2; i <= clean.Length - 2; i++)
        {
            var left = clean.Substring(0, i);
            var right = clean.Substring(i);
            if (dict.Contains(left) && dict.Contains(right))
            {
                return $"{left} {right}";
            }
        }
        for (int i = 2; i <= clean.Length - 4; i++)
        {
            var left = clean.Substring(0, i);
            if (dict.Contains(left))
            {
                for (int j = i + 1; j <= clean.Length - 2; j++)
                {
                    var mid = clean.Substring(i, j - i);
                    var right = clean.Substring(j);
                    if (dict.Contains(mid) && dict.Contains(right))
                    {
                        return $"{left} {mid} {right}";
                    }
                }
            }
        }
        return null;
    }

    public async Task<List<MediaItem>> SearchAnimeAsync(string query, string? source = null)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" && source != "Tutte le fonti" && source != "All sources" 
                ? $"-s \"{source}\"" : "";
            var output = await RunCommandAsync($"\"{query}\" -a {srcArg} --list");
            var items = ParseSearchOutput(output, "anime");
            if (items.Count == 0)
            {
                var segmented = GetSegmentedQuery(query);
                if (!string.IsNullOrWhiteSpace(segmented))
                {
                    var fallbackOutput = await RunCommandAsync($"\"{segmented}\" -a {srcArg} --list");
                    var fallbackItems = ParseSearchOutput(fallbackOutput, "anime");
                    if (fallbackItems.Count > 0) items = fallbackItems;
                }
            }

            EnrichCoversAsync(items, "anime");

            return items;
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
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" && source != "Tutte le fonti" && source != "All sources" 
                ? $"-s \"{source}\"" : "";
            var langArg = !string.IsNullOrWhiteSpace(lang) ? $"-l {lang}" : "-l it";
            var output = await RunCommandAsync($"\"{query}\" -m {srcArg} {langArg} --list");
            var items = ParseSearchOutput(output, "manga");
            if (items.Count == 0)
            {
                var segmented = GetSegmentedQuery(query);
                if (!string.IsNullOrWhiteSpace(segmented))
                {
                    var fallbackOutput = await RunCommandAsync($"\"{segmented}\" -m {srcArg} {langArg} --list");
                    var fallbackItems = ParseSearchOutput(fallbackOutput, "manga");
                    if (fallbackItems.Count > 0) items = fallbackItems;
                }
            }

            EnrichCoversAsync(items, "manga");

            return items;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching manga: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<int> GetMediaDetailsAsync(MediaItem item)
    {
        try
        {
            var typeArg = item.Type.ToLower() == "manga" ? "-m -l it" : "-a";
            var output = await RunCommandAsync($"\"{item.Title}\" {typeArg} -i");
            var m = Regex.Match(output, @"Total\s+(?:episodes|chapters):\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var total) && total > 0)
            {
                return total;
            }
            return 1;
        }
        catch
        {
            return 1;
        }
    }

    public async Task<(bool success, string message)> PlayStreamAsync(MediaItem item, int episode)
    {
        try
        {
            var output = await RunCommandAsync($"\"{item.Title}\" -a -e {episode} --get-url");
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var streamUrl = lines.FirstOrDefault(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                                      l.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(streamUrl))
            {
                var mpvPsi = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = $"\"{streamUrl.Trim()}\" --title=\"{item.Title} - Episode {episode}\" --force-window=immediate",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);
                return (true, $"Playing: {item.Title} (Episode {episode})");
            }

            return (false, "Could not extract video stream URL for this episode.");
        }
        catch (Exception ex)
        {
            return (false, $"Playback error: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> ReadMangaAsync(MediaItem item, int chapter)
    {
        try
        {
            var output = await RunCommandAsync($"\"{item.Title}\" -m -l it -C {chapter} --get-url");
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var pages = lines.Where(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                         l.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                             .Select(l => l.Trim())
                             .ToList();

            if (pages.Count > 0)
            {
                var pagesArgs = string.Join(" ", pages.Select(p => $"\"{p}\""));
                var mpvPsi = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = $"--title=\"{item.Title} - Chapter {chapter}\" --image-display-duration=inf --force-window=immediate {pagesArgs}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);
                return (true, $"Reader opened: {item.Title} (Chapter {chapter} - {pages.Count} pages)");
            }

            return (false, "No pages found for this chapter.");
        }
        catch (Exception ex)
        {
            return (false, $"Reading error: {ex.Message}");
        }
    }

    public async Task<List<MediaItem>> GetHistoryAsync()
    {
        var list = new List<MediaItem>();
        try
        {
            if (!File.Exists(_historyFile)) return list;

            var raw = await File.ReadAllTextAsync(_historyFile);
            using var doc = JsonDocument.Parse(raw);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var val = prop.Value;
                var isAnime = prop.Name.StartsWith("anime:") || 
                              (val.TryGetProperty("mediaType", out var mt) && mt.GetString() == "anime");

                var item = new MediaItem
                {
                    Id = prop.Name,
                    Title = val.TryGetProperty("title", out var t) ? t.GetString() ?? prop.Name : prop.Name,
                    Url = val.TryGetProperty("url", out var u) ? u.GetString() : prop.Name,
                    Source = val.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "",
                    SourceName = val.TryGetProperty("source", out var sn) ? sn.GetString() ?? "" : "",
                    Language = val.TryGetProperty("language", out var lang) ? lang.GetString() ?? "it" : "it",
                    Type = isAnime ? "anime" : "manga",
                    LastAccessedAt = isAnime ? 
                        (val.TryGetProperty("lastWatchedAt", out var lw) ? lw.GetString() : null) :
                        (val.TryGetProperty("lastReadAt", out var lr) ? lr.GetString() : null)
                };

                var numbers = new List<int>();
                var arrayName = isAnime ? "watchedEpisodes" : "readChapters";

                if (val.TryGetProperty(arrayName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in arr.EnumerateArray())
                    {
                        var idStr = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        var titleStr = elem.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
                        var num = ExtractNumber(idStr) ?? ExtractNumber(titleStr);
                        if (num.HasValue) numbers.Add(num.Value);
                    }
                }

                item.WatchedNumbers = numbers.Distinct().OrderBy(n => n).ToList();
                item.Count = item.WatchedNumbers.Count;
                item.UpdateWatchedSummary();
                item.Image = await CoverService.GetCoverAsync(item.Title, item.Type);

                list.Add(item);
            }

            list = list.OrderByDescending(i => DateTime.TryParse(i.LastAccessedAt, out var dt) ? dt : DateTime.MinValue).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading history: {ex.Message}");
        }

        return list;
    }

    public async Task<bool> UnmarkItemAsync(MediaItem item, int number)
    {
        try
        {
            var typeFlag = item.Type.ToLower() == "manga" ? "-m" : "-a";
            var output = await RunCommandAsync($"\"{item.Title}\" {typeFlag} --unmark {number}");
            return output.Contains("✔ Unmarked", StringComparison.OrdinalIgnoreCase) || 
                   output.Contains("Unmarked", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ClearAllHistoryAsync()
    {
        try
        {
            if (File.Exists(_historyFile))
            {
                await File.WriteAllTextAsync(_historyFile, "{}");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing history: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveFromHistoryAsync(MediaItem item)
    {
        try
        {
            var typeFlag = item.Type.ToLower() == "manga" ? "-m" : "-a";
            var output = await RunCommandAsync($"\"{item.Title}\" {typeFlag} --remove-history");
            return output.Contains("✔ Successfully removed", StringComparison.OrdinalIgnoreCase) || 
                   output.Contains("removed", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void EnrichCoversAsync(List<MediaItem> items, string type)
    {
        _ = Task.Run(async () =>
        {
            using var semaphore = new System.Threading.SemaphoreSlim(6);
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var cover = await CoverService.GetCoverAsync(item.Title, type);
                    if (!string.IsNullOrWhiteSpace(cover))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            item.Image = cover;
                        });
                    }
                }
                catch {}
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        });
    }

    private static List<MediaItem> ParseCatalogOutput(string output, string type)
    {
        var list = new List<MediaItem>();
        var currentSource = type == "anime" ? "One Piece Power Anime" : "One Piece Power";
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var srcMatch = Regex.Match(line, @"📂\s*\[(.*?)\]");
            if (srcMatch.Success)
            {
                currentSource = srcMatch.Groups[1].Value.Trim();
                continue;
            }

            var itemMatch = Regex.Match(line, @"^\s*\d+\.\s+(.*?)(?:\s+\[(.*?)\])?$");
            if (itemMatch.Success)
            {
                var title = itemMatch.Groups[1].Value.Trim();
                var tag = itemMatch.Groups[2].Success ? itemMatch.Groups[2].Value.Trim() : "";

                list.Add(new MediaItem
                {
                    Title = title,
                    Type = type,
                    Audio = tag,
                    Source = currentSource.ToLower().Replace(" ", ""),
                    SourceName = currentSource
                });
            }
        }

        return list;
    }

    private static List<MediaItem> ParseSearchOutput(string output, string type)
    {
        var list = new List<MediaItem>();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^\s*-\s+(.*?)(?:\s+\[(.*?)\])?(?:\s+\[(.*?)\])?$");
            if (m.Success)
            {
                var title = m.Groups[1].Value.Trim();
                var tag1 = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                var tag2 = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";

                var audio = tag1;
                var source = !string.IsNullOrWhiteSpace(tag2) ? tag2 : "One Piece Power";

                list.Add(new MediaItem
                {
                    Title = title,
                    Type = type,
                    Audio = audio,
                    Source = source.ToLower().Replace(" ", ""),
                    SourceName = source
                });
            }
        }

        return list;
    }

    private static int? ExtractNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"\b(\d+)\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
        {
            return n;
        }
        return null;
    }

    private static void PassDisplayEnv(ProcessStartInfo psi)
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display)) psi.Environment["DISPLAY"] = display;

        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!string.IsNullOrEmpty(wayland)) psi.Environment["WAYLAND_DISPLAY"] = wayland;

        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg)) psi.Environment["XDG_RUNTIME_DIR"] = xdg;
    }
}
