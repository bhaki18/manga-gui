using System.Text;
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
        _historyFile = ResolveHistoryFilePath();
    }

    public static string ResolveHistoryFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var user = Environment.UserName;
            var wineHost = Path.Combine("Z:", "home", user, ".local", "state", "manga-cli", "history.json");
            if (File.Exists(wineHost)) return wineHost;

            var envUser = Environment.GetEnvironmentVariable("USER") ?? Environment.GetEnvironmentVariable("USERNAME");
            if (!string.IsNullOrEmpty(envUser))
            {
                var wineHostEnv = Path.Combine("Z:", "home", envUser, ".local", "state", "manga-cli", "history.json");
                if (File.Exists(wineHostEnv)) return wineHostEnv;
            }

            if (Directory.Exists(Path.Combine("Z:", "home", user)))
            {
                var wineHostDir = Path.Combine("Z:", "home", user, ".local", "state", "manga-cli");
                Directory.CreateDirectory(wineHostDir);
                return Path.Combine(wineHostDir, "history.json");
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var standard = Path.Combine(home, ".local", "state", "manga-cli", "history.json");
        if (File.Exists(standard)) return standard;

        var dir = Path.GetDirectoryName(standard);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return standard;
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

        private async Task<string> RunCommandWithProgressAsync(string arguments, Action<string>? onProgress = null)
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
            psi.FileName = ResolveNodePath();
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

        using var process = new Process { StartInfo = psi };
        var sb = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                sb.AppendLine(e.Data);
                if (e.Data.Contains("[PROGRESS]"))
                {
                    var prog = e.Data.Substring(e.Data.IndexOf("[PROGRESS]") + 10).Trim();
                    onProgress?.Invoke(prog);
                }
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                sb.AppendLine(e.Data);
                if (e.Data.Contains("[PROGRESS]"))
                {
                    var prog = e.Data.Substring(e.Data.IndexOf("[PROGRESS]") + 10).Trim();
                    onProgress?.Invoke(prog);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return sb.ToString().Trim();
    }

    private async Task<string> RunCommandAsync(string arguments)
    {
        return await RunCommandWithProgressAsync(arguments);
    }

    
    private static string EscapeArg(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\"", "\\\"");
    }

    public static string NormalizeProviderSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.ToLowerInvariant().Trim();
        if (s.Contains("mangadex")) return "mangadex";
        if (s.Contains("mangapill")) return "mangapill";
        if (s.Contains("animeworld")) return "animeworld";
        if (s.Contains("animesaturn")) return "animesaturn";
        if (s.Contains("onepiecepower") || s.Contains("one piece power") || s.Contains("oppower"))
            return s.Contains("anime") ? "onepiecepower-anime" : "onepiecepower";
        return s.Replace(" ", "");
    }

    public static string FormatSourceName(string? source)
    {
        var norm = NormalizeProviderSource(source);
        return norm switch
        {
            "mangadex" => "MangaDex",
            "mangapill" => "Mangapill",
            "animeworld" => "AnimeWorld",
            "onepiecepower-anime" => "One Piece Power Anime",
            "onepiecepower" => "One Piece Power",
            _ => !string.IsNullOrWhiteSpace(source) ? source : "Unknown"
        };
    }

    private static List<MediaItem> ParseJsonMediaItems(string output, string type)
    {
        var list = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        try
        {
            var startIdx = output.IndexOf('[');
            var endIdx = output.LastIndexOf(']');
            if (startIdx < 0 || endIdx <= startIdx) return list;

            var jsonSlice = output.Substring(startIdx, endIdx - startIdx + 1);
            using var doc = JsonDocument.Parse(jsonSlice);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (elem.ValueKind != JsonValueKind.Object) continue;

                var title = elem.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String
                    ? tProp.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title)) continue;

                var url = elem.TryGetProperty("url", out var uProp) && uProp.ValueKind == JsonValueKind.String
                    ? uProp.GetString() : null;
                var id = elem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString() : (url ?? title);
                var rawSource = elem.TryGetProperty("source", out var sProp) && sProp.ValueKind == JsonValueKind.String
                    ? sProp.GetString() ?? "" : "";
                var rawSourceName = elem.TryGetProperty("sourceName", out var snProp) && snProp.ValueKind == JsonValueKind.String
                    ? snProp.GetString() ?? "" : "";
                var normSource = NormalizeProviderSource(!string.IsNullOrWhiteSpace(rawSource) ? rawSource : rawSourceName);
                var displaySourceName = !string.IsNullOrWhiteSpace(rawSourceName) ? rawSourceName : FormatSourceName(normSource);

                var lang = elem.TryGetProperty("language", out var lProp) && lProp.ValueKind == JsonValueKind.String
                    ? lProp.GetString() ?? "it" : "it";
                var audio = elem.TryGetProperty("audio", out var aProp) && aProp.ValueKind == JsonValueKind.String
                    ? aProp.GetString() ?? "" : "";
                var author = elem.TryGetProperty("author", out var auProp) && auProp.ValueKind == JsonValueKind.String
                    ? auProp.GetString() ?? "" : "";

                string genres = "";
                if (elem.TryGetProperty("genres", out var gProp))
                {
                    if (gProp.ValueKind == JsonValueKind.String)
                        genres = gProp.GetString() ?? "";
                    else if (gProp.ValueKind == JsonValueKind.Array)
                        genres = string.Join(", ", gProp.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                }

                list.Add(new MediaItem
                {
                    Id = id ?? title,
                    Title = title,
                    Url = url,
                    Type = type,
                    Source = normSource,
                    SourceName = displaySourceName,
                    Language = lang,
                    Audio = !string.IsNullOrWhiteSpace(audio) ? audio : lang.ToUpperInvariant(),
                    Author = author,
                    Genres = genres
                });
            }
        }
        catch
        {
            // Fallback handled by caller
        }

        return list;
    }

    private static List<string> ExtractPagesFromOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return new List<string>();
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                     .Where(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                 l.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                     .Select(l => l.Trim())
                     .ToList();
    }

    private static string GetReferrerForPages(List<string> pages, string provider)
    {
        if (pages == null || pages.Count == 0) return "";
        var p0 = pages[0].ToLowerInvariant();
        if (p0.Contains("readdetectiveconan") || p0.Contains("mangapill") || provider.Contains("mangapill"))
            return "https://mangapill.com/";
        if (p0.Contains("mangadex") || provider.Contains("mangadex"))
            return "https://mangadex.org/";
        if (p0.Contains("onepiecepower") || provider.Contains("onepiecepower"))
            return "https://onepiecepower.com/";
        if (p0.Contains("animeworld") || provider.Contains("animeworld"))
            return "https://www.animeworld.ac/";
        try
        {
            var uri = new Uri(pages[0]);
            return $"{uri.Scheme}://{uri.Host}/";
        }
        catch
        {
            return "";
        }
    }

    public static string CleanMediaTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var t = Regex.Replace(raw, @"\s*\[[^\]]+\]", "").Trim();
        t = Regex.Replace(t, @"\s*\((?:[A-Za-z0-9_\-,\s/]+)\)$", "").Trim();
        return string.IsNullOrWhiteSpace(t) ? raw.Trim() : t;
    }

    public static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(ch => !invalid.Contains(ch) && ch != '/' && ch != '\\' && ch != ':' && ch != '*' && ch != '?' && ch != '"' && ch != '<' && ch != '>' && ch != '|').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "unnamed" : clean;
    }

    private static bool HasActualDownloads(string basePath)
    {
        try
        {
            if (!Directory.Exists(basePath)) return false;
            var anime = Path.Combine(basePath, "Anime");
            if (Directory.Exists(anime) && Directory.GetFileSystemEntries(anime).Length > 0) return true;
            var manga = Path.Combine(basePath, "Manga");
            if (Directory.Exists(manga) && Directory.GetFileSystemEntries(manga).Length > 0) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static string GetBaseDownloadDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var user = Environment.UserName;
            var wineHost = Path.Combine("Z:", "home", user, ".config", "manga-gui", "Downloaded");
            if (HasActualDownloads(wineHost)) return wineHost;

            var envUser = Environment.GetEnvironmentVariable("USER") ?? Environment.GetEnvironmentVariable("USERNAME");
            if (!string.IsNullOrEmpty(envUser))
            {
                var wineHostEnv = Path.Combine("Z:", "home", envUser, ".config", "manga-gui", "Downloaded");
                if (HasActualDownloads(wineHostEnv)) return wineHostEnv;
            }

            if (Directory.Exists(Path.Combine("Z:", "home", user)))
            {
                Directory.CreateDirectory(wineHost);
                return wineHost;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var standard = Path.Combine(home, ".config", "manga-gui", "Downloaded");
        if (HasActualDownloads(standard)) return standard;

        var alt = Path.Combine(home, ".config", "manga-gui", "Dowloaded");
        if (Directory.Exists(alt) && !Directory.Exists(standard)) return alt;

        Directory.CreateDirectory(standard);
        return standard;
    }

    public static string GetAnimeDownloadDirectory()
    {
        var dir = Path.Combine(GetBaseDownloadDirectory(), "Anime");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetMangaDownloadDirectory()
    {
        var dir = Path.Combine(GetBaseDownloadDirectory(), "Manga");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<(bool success, string message, string? destination)> DownloadMediaAsync(MediaItem item, int number, Action<string>? onProgress = null)
    {
        try
        {
            var isManga = item.Type.ToLower() == "manga";
            var baseDir = isManga ? GetMangaDownloadDirectory() : GetAnimeDownloadDirectory();
            var cleanTitle = CleanMediaTitle(item.Title);
            var safeTitle = SanitizeFilename(cleanTitle);
            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "unnamed";
            var targetDir = Path.Combine(baseDir, safeTitle);
            Directory.CreateDirectory(targetDir);

            var langArg = !string.IsNullOrWhiteSpace(item.Language) && item.Language.ToLower() != "it" 
                ? $"-l {item.Language}" 
                : "-l all";
            var src = NormalizeProviderSource(item.Source);
            var srcArg = !string.IsNullOrWhiteSpace(src) && src != "all" && src != "downloaded" ? $"-s \"{src}\"" : "";
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) && item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"--url \"{EscapeArg(item.Url)}\"" : "";
            var typeArg = isManga
                ? $"-m {srcArg} {urlArg} {langArg} -C {number} -d \"{targetDir}\""
                : $"-a {srcArg} {urlArg} -e {number} -d \"{targetDir}\"";
            var output = await RunCommandWithProgressAsync($"\"{EscapeArg(cleanTitle)}\" {typeArg}", onProgress);
            
            var match = Regex.Match(output, @"Destination:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var dest = match.Success ? match.Groups[1].Value.Trim() : null;

            if (output.Contains("Download complete") || output.Contains("Downloaded all") || 
                (dest != null && (File.Exists(dest) || Directory.Exists(dest))))
            {
                var unit = isManga ? "Chapter" : "Episode";
                return (true, $"✔ Download complete: {item.Title} ({unit} {number})", dest);
            }

            if (output.Contains("already exists") || output.Contains("100%"))
            {
                var unit = isManga ? "Chapter" : "Episode";
                return (true, $"✔ Already downloaded: {item.Title} ({unit} {number})", dest);
            }

            return (false, $"Download completed with message: {output.Trim()}", dest);
        }
        catch (Exception ex)
        {
            return (false, $"Download error: {ex.Message}", null);
        }
    }

    public async Task<List<MediaItem>> GetDownloadedItemsAsync()
    {
        var items = new List<MediaItem>();
        try
        {
            var animeDir = GetAnimeDownloadDirectory();
            if (Directory.Exists(animeDir))
            {
                foreach (var dir in Directory.GetDirectories(animeDir))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var videoFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                            .Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                        f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                        f.Extension.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (videoFiles.Count > 0)
                        {
                            long totalBytes = videoFiles.Sum(f => f.Length);
                            var title = dirInfo.Name;
                            var numbers = new List<int>();

                            foreach (var vf in videoFiles)
                            {
                                var m = Regex.Match(vf.Name, @"(?:ep(?:isode)?|cap(?:itolo)?|\b)(\d+)", RegexOptions.IgnoreCase);
                                if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
                                {
                                    numbers.Add(n);
                                }
                            }
                            if (numbers.Count == 0) numbers.Add(1);

                            var item = new MediaItem
                            {
                                Id = $"download:anime:{title}",
                                Title = title,
                                Type = "anime",
                                Source = "downloaded",
                                SourceName = "Offline Storage",
                                IsDownloaded = true,
                                LocalPath = dir,
                                Count = videoFiles.Count,
                                WatchedNumbers = numbers.Distinct().OrderBy(x => x).ToList(),
                                DownloadSizeText = FormatFileSize(totalBytes),
                                CustomSubDubText = $"{videoFiles.Count} {(videoFiles.Count == 1 ? "episode" : "episodes")} ({FormatFileSize(totalBytes)})"
                            };

                            var localCover = Path.Combine(dir, "cover.jpg");
                            if (File.Exists(localCover))
                            {
                                item.Image = localCover;
                            }

                            items.Add(item);
                        }
                    }
                    catch {}
                }
            }

            var mangaDir = GetMangaDownloadDirectory();
            if (Directory.Exists(mangaDir))
            {
                foreach (var dir in Directory.GetDirectories(mangaDir))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var subDirs = dirInfo.GetDirectories();
                        var imgFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                            .Where(f => f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                        f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                        f.Extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (subDirs.Length > 0 || imgFiles.Count > 0)
                        {
                            long totalBytes = imgFiles.Sum(f => f.Length);
                            var title = dirInfo.Name;
                            var count = subDirs.Length > 0 ? subDirs.Length : 1;
                            var numbers = new List<int>();

                            foreach (var sd in subDirs)
                            {
                                var m = Regex.Match(sd.Name, @"(?:cap(?:itolo)?|ch(?:apter)?|\b)(\d+)", RegexOptions.IgnoreCase);
                                if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
                                {
                                    numbers.Add(n);
                                }
                            }
                            if (numbers.Count == 0) numbers.Add(1);

                            var item = new MediaItem
                            {
                                Id = $"download:manga:{title}",
                                Title = title,
                                Type = "manga",
                                Source = "downloaded",
                                SourceName = "Offline Storage",
                                IsDownloaded = true,
                                LocalPath = dir,
                                Count = count,
                                WatchedNumbers = numbers.Distinct().OrderBy(x => x).ToList(),
                                DownloadSizeText = FormatFileSize(totalBytes),
                                CustomSubDubText = $"{count} {(count == 1 ? "chapter" : "chapters")} ({FormatFileSize(totalBytes)})"
                            };

                            var localCover = Path.Combine(dir, "cover.jpg");
                            if (File.Exists(localCover))
                            {
                                item.Image = localCover;
                            }

                            items.Add(item);
                        }
                    }
                    catch {}
                }
            }

            EnrichCoversAsync(items, "anime");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning downloads: {ex.Message}");
        }
        return items;
    }

    public async Task<bool> DeleteDownloadedItemAsync(MediaItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.LocalPath) && Directory.Exists(item.LocalPath))
            {
                Directory.Delete(item.LocalPath, true);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting download: {ex.Message}");
            return false;
        }
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1000)
        {
            return $"{(mb / 1024.0):F1} GB";
        }
        return $"{mb:F1} MB";
    }

    public async Task<List<MediaItem>> GetAnimeCatalogAsync(string? source = null)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(source) && source != "all" && source != "Tutte le fonti" && source != "All sources" 
                ? $"-s \"{EscapeArg(source)}\"" : "";
            var output = await RunCommandAsync($"--catalog-anime {srcArg} --json");
            var items = ParseJsonMediaItems(output, "anime");
            if (items.Count == 0)
            {
                var fallbackOutput = await RunCommandAsync($"--catalog-anime {srcArg} --list");
                items = ParseCatalogOutput(fallbackOutput, "anime");
            }

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
                ? $"-s \"{EscapeArg(source)}\"" : "";
            var output = await RunCommandAsync($"--catalog-manga {srcArg} --json");
            var items = ParseJsonMediaItems(output, "manga");
            if (items.Count == 0)
            {
                var fallbackOutput = await RunCommandAsync($"--catalog-manga {srcArg} --list");
                items = ParseCatalogOutput(fallbackOutput, "manga");
            }

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
                ? $"-s \"{EscapeArg(source)}\"" : "";
            var output = await RunCommandAsync($"\"{EscapeArg(query)}\" -a {srcArg} --list --json");
            var items = ParseJsonMediaItems(output, "anime");
            if (items.Count == 0 && !output.TrimStart().StartsWith("["))
            {
                items = ParseSearchOutput(output, "anime");
            }
            if (items.Count == 0)
            {
                var segmented = GetSegmentedQuery(query);
                if (!string.IsNullOrWhiteSpace(segmented))
                {
                    var fallbackOutput = await RunCommandAsync($"\"{EscapeArg(segmented)}\" -a {srcArg} --list --json");
                    var fallbackItems = ParseJsonMediaItems(fallbackOutput, "anime");
                    if (fallbackItems.Count == 0 && !fallbackOutput.TrimStart().StartsWith("["))
                    {
                        fallbackItems = ParseSearchOutput(fallbackOutput, "anime");
                    }
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
                ? $"-s \"{EscapeArg(source)}\"" : "";
            var langArg = !string.IsNullOrWhiteSpace(lang) ? $"-l {lang}" : "-l all";
            var output = await RunCommandAsync($"\"{EscapeArg(query)}\" -m {srcArg} {langArg} --list --json");
            var items = ParseJsonMediaItems(output, "manga");
            if (items.Count == 0 && !output.TrimStart().StartsWith("["))
            {
                items = ParseSearchOutput(output, "manga");
            }
            if (items.Count == 0)
            {
                var segmented = GetSegmentedQuery(query);
                if (!string.IsNullOrWhiteSpace(segmented))
                {
                    var fallbackOutput = await RunCommandAsync($"\"{EscapeArg(segmented)}\" -m {srcArg} {langArg} --list --json");
                    var fallbackItems = ParseJsonMediaItems(fallbackOutput, "manga");
                    if (fallbackItems.Count == 0 && !fallbackOutput.TrimStart().StartsWith("["))
                    {
                        fallbackItems = ParseSearchOutput(fallbackOutput, "manga");
                    }
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
            var isManga = item.Type.ToLower() == "manga";
            var cleanTitle = CleanMediaTitle(item.Title);
            var src = NormalizeProviderSource(item.Source);
            var srcArg = !string.IsNullOrWhiteSpace(src) && src != "all" && src != "downloaded"
                ? $"-s \"{src}\"" : "";
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) && item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"--url \"{EscapeArg(item.Url)}\"" : "";
            var langArg = isManga
                ? (!string.IsNullOrWhiteSpace(item.Language) && item.Language.ToLower() != "it" ? $"-l {item.Language}" : "-l all")
                : "";
            var typeArg = isManga ? $"-m {srcArg} {urlArg} {langArg}" : $"-a {srcArg} {urlArg}";
            var output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" {typeArg} -i");
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
            var cleanTitle = CleanMediaTitle(item.Title);
            var src = NormalizeProviderSource(item.Source);
            var srcArg = !string.IsNullOrWhiteSpace(src) && src != "all" && src != "downloaded"
                ? $"-s \"{src}\"" : "";
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) && item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"--url \"{EscapeArg(item.Url)}\"" : "";

            var output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" -a {srcArg} {urlArg} -e {episode} --get-url");
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var streamUrl = lines.FirstOrDefault(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                                      l.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            // Fallback across all anime providers if primary source/URL failed
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" -a -e {episode} --get-url");
                lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                streamUrl = lines.FirstOrDefault(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                                      l.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(streamUrl))
            {
                var trimmedUrl = streamUrl.Trim();
                var referrer = GetReferrerForPages(new List<string> { trimmedUrl }, src);
                var refArg = !string.IsNullOrWhiteSpace(referrer) ? $"--referrer=\"{referrer}\"" : "";
                var uaArg = "--user-agent=\"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36\"";

                var mpvPsi = new ProcessStartInfo
                {
                    FileName = ResolveMpvPath(),
                    Arguments = $"\"{trimmedUrl}\" --title=\"{EscapeArg(item.Title)} - Episode {episode}\" {refArg} {uaArg} --force-window=immediate",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);

                if (!item.WatchedNumbers.Contains(episode))
                {
                    item.WatchedNumbers.Add(episode);
                    item.WatchedNumbers.Sort();
                    item.UpdateWatchedSummary();
                }

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
            // 1. Check if chapter is already downloaded locally in ~/.config/manga-gui/Downloaded/Manga/<Title>/
            var mangaDir = GetMangaDownloadDirectory();
            var safeTitle = new string(item.Title.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_').ToArray()).Trim();
            var titleDir = Path.Combine(mangaDir, safeTitle);
            if (Directory.Exists(titleDir))
            {
                var subDirs = Directory.GetDirectories(titleDir);
                var localChapterDir = subDirs.FirstOrDefault(d => 
                    Regex.IsMatch(Path.GetFileName(d), $@"(?:cap(?:itolo)?|ch(?:apter)?| )0*{chapter} ", RegexOptions.IgnoreCase));

                if (localChapterDir != null && Directory.Exists(localChapterDir))
                {
                    var localPsi = new ProcessStartInfo
                    {
                        FileName = ResolveMpvPath(),
                        Arguments = $"--title=\"{EscapeArg(item.Title)} - Chapter {chapter} (Offline)\" --image-display-duration=inf \"{localChapterDir}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        CreateNoWindow = false
                    };
                    PassDisplayEnv(localPsi);
                    Process.Start(localPsi);
                    return (true, $"Reader opened offline: {item.Title} (Chapter {chapter})");
                }
            }

            // 2. Fetch URLs from provider with multi-tier provider fallback
            var cleanTitle = CleanMediaTitle(item.Title);
            var src = NormalizeProviderSource(item.Source);
            var srcArg = !string.IsNullOrWhiteSpace(src) && src != "all" && src != "downloaded"
                ? $"-s \"{src}\"" : "";
            var urlArg = !string.IsNullOrWhiteSpace(item.Url) && item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"--url \"{EscapeArg(item.Url)}\"" : "";
            var langArg = !string.IsNullOrWhiteSpace(item.Language) && item.Language.ToLower() != "it" 
                ? $"-l {item.Language}" 
                : "-l all";

            var output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" -m {srcArg} {urlArg} {langArg} -C {chapter} --get-url");
            var pages = ExtractPagesFromOutput(output);

            // Fallback 1: Try onepiecepower (Italian scanlations) if primary failed
            if (pages.Count == 0 && src != "onepiecepower")
            {
                output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" -m -s onepiecepower -l all -C {chapter} --get-url");
                pages = ExtractPagesFromOutput(output);
            }

            // Fallback 2: Try mangapill (free fast English scans)
            if (pages.Count == 0 && src != "mangapill")
            {
                output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" -m -s mangapill -l en -C {chapter} --get-url");
                pages = ExtractPagesFromOutput(output);
            }

            // Fallback 3: Try general search across all providers
            if (pages.Count == 0)
            {
                output = await RunCommandAsync($"\"{EscapeArg(cleanTitle)}\" -m -l all -C {chapter} --get-url");
                pages = ExtractPagesFromOutput(output);
            }

            if (pages.Count > 0)
            {
                // Write playlist to temp file for clean and fast MPV loading
                var playlistDir = Path.Combine(Path.GetTempPath(), "manga-gui");
                Directory.CreateDirectory(playlistDir);
                var playlistFile = Path.Combine(playlistDir, $"playlist_{safeTitle}_{chapter}.m3u");
                await File.WriteAllLinesAsync(playlistFile, pages);

                var referrer = GetReferrerForPages(pages, src);
                var refArg = !string.IsNullOrWhiteSpace(referrer) ? $"--referrer=\"{referrer}\"" : "";
                var uaArg = "--user-agent=\"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36\"";

                var mpvPsi = new ProcessStartInfo
                {
                    FileName = ResolveMpvPath(),
                    Arguments = $"--title=\"{EscapeArg(item.Title)} - Chapter {chapter}\" --image-display-duration=inf {refArg} {uaArg} --playlist=\"{playlistFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false
                };
                PassDisplayEnv(mpvPsi);
                Process.Start(mpvPsi);

                if (!item.WatchedNumbers.Contains(chapter))
                {
                    item.WatchedNumbers.Add(chapter);
                    item.WatchedNumbers.Sort();
                    item.UpdateWatchedSummary();
                }

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

            var itemMatch = Regex.Match(line, @"^\s*\d+\.\s+(.*?)\s+\[([^\]]+)\](?:\s+\((.*?)\))?$");
            if (itemMatch.Success)
            {
                var title = itemMatch.Groups[1].Value.Trim();
                var tag = itemMatch.Groups[2].Value.Trim();
                var genres = itemMatch.Groups[3].Success ? itemMatch.Groups[3].Value.Trim() : "";

                list.Add(new MediaItem
                {
                    Title = title,
                    Type = type,
                    Audio = tag,
                    Language = tag.ToLower(),
                    Genres = genres,
                    Source = NormalizeProviderSource(currentSource),
                    SourceName = currentSource
                });
            }
            else
            {
                var simpleMatch = Regex.Match(line, @"^\s*\d+\.\s+(.*?)(?:\s+\((.*?)\))?$");
                if (simpleMatch.Success)
                {
                    list.Add(new MediaItem
                    {
                        Title = simpleMatch.Groups[1].Value.Trim(),
                        Type = type,
                        Source = NormalizeProviderSource(currentSource),
                        SourceName = currentSource
                    });
                }
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
            var m = Regex.Match(line, @"^\s*-\s+(.*?)(?:\s+\[([^\]]+)\])?(?:\s+\[([^\]]+)\])?$");
            if (m.Success)
            {
                var title = m.Groups[1].Value.Trim();
                var tag1 = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                var tag2 = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";

                string audio;
                string sourceRaw;
                if (!string.IsNullOrWhiteSpace(tag2))
                {
                    audio = tag1;
                    sourceRaw = tag2;
                }
                else if (type == "manga" && !string.IsNullOrWhiteSpace(tag1))
                {
                    audio = "";
                    sourceRaw = tag1;
                }
                else
                {
                    audio = tag1;
                    sourceRaw = "One Piece Power";
                }

                var normSource = NormalizeProviderSource(sourceRaw);
                list.Add(new MediaItem
                {
                    Title = title,
                    Type = type,
                    Audio = audio,
                    Source = normSource,
                    SourceName = FormatSourceName(normSource)
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

    public static string ResolveMpvPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        var local = Path.Combine(baseDir, exeName);
        if (File.Exists(local)) return local;
        return exeName;
    }

    public static string ResolveNodePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var local = Path.Combine(baseDir, exeName);
        if (File.Exists(local)) return local;
        return exeName;
    }

    public static void PassDisplayEnv(ProcessStartInfo psi)
    {
        if (OperatingSystem.IsWindows()) return;
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display)) psi.Environment["DISPLAY"] = display;

        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!string.IsNullOrEmpty(wayland)) psi.Environment["WAYLAND_DISPLAY"] = wayland;

        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg)) psi.Environment["XDG_RUNTIME_DIR"] = xdg;
    }

}
