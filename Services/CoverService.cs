using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MangaGui.Services;

public class CoverService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };
    private static readonly ConcurrentDictionary<string, string> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Threading.SemaphoreSlim _coverLock = new(2, 2);
    private static readonly string _cacheFile;

    static CoverService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json, application/json");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".local", "state", "manga-cli");

        if (OperatingSystem.IsWindows())
        {
            var user = Environment.UserName;
            var wineHostDir = Path.Combine("Z:", "home", user, ".local", "state", "manga-cli");
            if (Directory.Exists(wineHostDir))
            {
                dir = wineHostDir;
            }
        }

        Directory.CreateDirectory(dir);

        // Remove legacy v1 cache that contained mismatched Kitsu fallback posters
        var legacyCacheFile = Path.Combine(dir, "covers.json");
        try
        {
            if (File.Exists(legacyCacheFile))
            {
                File.Delete(legacyCacheFile);
            }
        }
        catch { }

        _cacheFile = Path.Combine(dir, "covers_v2.json");
        LoadDiskCache();
    }

    private static void LoadDiskCache()
    {
        try
        {
            if (File.Exists(_cacheFile))
            {
                var json = File.ReadAllText(_cacheFile);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            _memoryCache[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void SaveDiskCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_memoryCache);
            File.WriteAllText(_cacheFile, json);
        }
        catch { }
    }

    public static async Task<string?> GetCoverAsync(string title, string type = "anime")
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var clean = CleanTitle(title);
        if (string.IsNullOrWhiteSpace(clean)) return null;

        var cacheKey = $"{type.ToLowerInvariant()}:{clean.ToLowerInvariant()}";

        if (_memoryCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        await _coverLock.WaitAsync();
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            // 1. Try AniList first (supports Romaji, English, and Synonyms with high-res covers)
            var url = await QueryAniListAsync(clean, type);

            // 2. Try Kitsu with strict title verification
            if (string.IsNullOrWhiteSpace(url))
            {
                url = await QueryKitsuAsync(clean, type);
            }

            // 3. If title is a special edition or spin-off (e.g. "One Piece - 1000 Story", "Blue Lock Full Color Selection"), try base series title
            if (string.IsNullOrWhiteSpace(url))
            {
                var baseTitle = ExtractBaseSeriesTitle(title, clean);
                if (!string.IsNullOrWhiteSpace(baseTitle) &&
                    !string.Equals(baseTitle, clean, StringComparison.OrdinalIgnoreCase) &&
                    baseTitle.Length >= 3)
                {
                    url = await QueryAniListAsync(baseTitle, type)
                          ?? await QueryKitsuAsync(baseTitle, type);
                }
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                _memoryCache[cacheKey] = url;
                SaveDiskCache();
                return url;
            }

            return null;
        }
        finally
        {
            await Task.Delay(100);
            _coverLock.Release();
        }
    }

    private static string? ExtractBaseSeriesTitle(string rawTitle, string cleanTitle)
    {
        // Check explicit separator in raw title ("Series - Subtitle" or "Series: Subtitle")
        var sepMatch = Regex.Split(rawTitle, @"\s+(?:[-–—:]|vol\.?\s*\d+)\s*", RegexOptions.IgnoreCase);
        if (sepMatch.Length > 1)
        {
            var candidate = CleanTitle(sepMatch[0]);
            if (candidate.Length >= 3) return candidate;
        }

        // Strip edition suffixes like "Colored Edition", "Full Color Selection", "Mini Avventure"
        var stripped = Regex.Replace(
            cleanTitle,
            @"\b(colored\s+edition|full\s+color(?:\s+selection)?|digital\s+colored(?:\s+comics)?|mini\s+avventure|party|episode\s+a|roman\s+album)\b.*$",
            "",
            RegexOptions.IgnoreCase
        ).Trim();

        if (!string.IsNullOrWhiteSpace(stripped) && stripped.Length >= 3 && stripped.Length < cleanTitle.Length)
        {
            return stripped;
        }

        return null;
    }

    private static async Task<string?> QueryKitsuAsync(string cleanTitle, string type)
    {
        try
        {
            var endpoint = type.ToLowerInvariant() == "manga" ? "manga" : "anime";
            var reqUrl = $"https://kitsu.io/api/edge/{endpoint}?filter[text]={Uri.EscapeDataString(cleanTitle)}&page[limit]=6";
            var res = await _httpClient.GetAsync(reqUrl);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (!item.TryGetProperty("attributes", out var attrs)) continue;

                        string? itemPosterUrl = null;
                        if (attrs.TryGetProperty("posterImage", out var poster) &&
                            poster.ValueKind == JsonValueKind.Object)
                        {
                            itemPosterUrl = poster.TryGetProperty("large", out var l) ? l.GetString() :
                                            poster.TryGetProperty("medium", out var m) ? m.GetString() :
                                            poster.TryGetProperty("original", out var o) ? o.GetString() :
                                            poster.TryGetProperty("small", out var s) ? s.GetString() : null;
                        }

                        if (string.IsNullOrWhiteSpace(itemPosterUrl)) continue;

                        var canonical = attrs.TryGetProperty("canonicalTitle", out var ct) ? ct.GetString() : null;
                        bool matched = IsTitleMatch(cleanTitle, canonical);

                        if (!matched && attrs.TryGetProperty("titles", out var titlesObj) && titlesObj.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in titlesObj.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == JsonValueKind.String && IsTitleMatch(cleanTitle, prop.Value.GetString()))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                        }

                        if (!matched && attrs.TryGetProperty("abbreviatedTitles", out var abbrArr) && abbrArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var abbr in abbrArr.EnumerateArray())
                            {
                                if (abbr.ValueKind == JsonValueKind.String && IsTitleMatch(cleanTitle, abbr.GetString()))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                        }

                        if (matched)
                        {
                            return itemPosterUrl;
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static async Task<string?> QueryAniListAsync(string cleanTitle, string type)
    {
        try
        {
            var mediaType = type.ToLowerInvariant() == "manga" ? "MANGA" : "ANIME";
            var query = @"query ($search: String, $type: MediaType) {
                Page(perPage: 5) {
                    media(search: $search, type: $type) {
                        title { romaji english native }
                        synonyms
                        coverImage { extraLarge large medium }
                    }
                }
            }";
            var payload = new
            {
                query,
                variables = new { search = cleanTitle, type = mediaType }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await _httpClient.PostAsync("https://graphql.anilist.co", content);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("Page", out var page) &&
                    page.TryGetProperty("media", out var mediaArr) &&
                    mediaArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var media in mediaArr.EnumerateArray())
                    {
                        var candidates = new List<string?>();
                        if (media.TryGetProperty("title", out var tObj) && tObj.ValueKind == JsonValueKind.Object)
                        {
                            if (tObj.TryGetProperty("romaji", out var r)) candidates.Add(r.GetString());
                            if (tObj.TryGetProperty("english", out var e)) candidates.Add(e.GetString());
                            if (tObj.TryGetProperty("native", out var n)) candidates.Add(n.GetString());
                        }
                        if (media.TryGetProperty("synonyms", out var synArr) && synArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var syn in synArr.EnumerateArray())
                            {
                                if (syn.ValueKind == JsonValueKind.String) candidates.Add(syn.GetString());
                            }
                        }

                        if (candidates.Any(c => IsTitleMatch(cleanTitle, c)))
                        {
                            if (media.TryGetProperty("coverImage", out var cover) && cover.ValueKind == JsonValueKind.Object)
                            {
                                var imgUrl = cover.TryGetProperty("large", out var l) ? l.GetString() :
                                             cover.TryGetProperty("extraLarge", out var xl) ? xl.GetString() :
                                             cover.TryGetProperty("medium", out var m) ? m.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(imgUrl))
                                {
                                    return imgUrl;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsTitleMatch(string query, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var qNorm = NormalizeForMatch(query);
        var cNorm = NormalizeForMatch(candidate);

        if (string.IsNullOrWhiteSpace(qNorm) || string.IsNullOrWhiteSpace(cNorm)) return false;
        if (qNorm == cNorm) return true;

        // Compact alphanumeric comparison (handles "One-Punch Man" vs "One Punch Man", "Hunter x Hunter", etc.)
        var qCompact = qNorm.Replace(" ", "");
        var cCompact = cNorm.Replace(" ", "");
        if (qCompact.Length >= 4 && qCompact == cCompact) return true;

        var qWords = qNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cWords = cNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (qWords.Length == 0 || cWords.Length == 0) return false;

        // Single-word query or candidate must match exactly (prevents "Treasures" matching "One Piece 10th Treasures")
        if (qWords.Length == 1 || cWords.Length == 1)
        {
            return qWords.Length == 1 && cWords.Length == 1 && qWords[0] == cWords[0];
        }

        // First significant word must match or be present in both
        var qSig = qWords.Where(w => w.Length > 2).ToArray();
        var cSig = cWords.Where(w => w.Length > 2).ToArray();
        if (qSig.Length == 0) qSig = qWords;
        if (cSig.Length == 0) cSig = cWords;

        var qSet = new HashSet<string>(qSig, StringComparer.OrdinalIgnoreCase);
        var cSet = new HashSet<string>(cSig, StringComparer.OrdinalIgnoreCase);

        int qInC = qSig.Count(w => cSet.Contains(w));
        int cInQ = cSig.Count(w => qSet.Contains(w));

        double qRatio = (double)qInC / qSig.Length;
        double cRatio = (double)cInQ / cSig.Length;

        return qRatio >= 0.75 && cRatio >= 0.6;
    }

    private static string NormalizeForMatch(string str)
    {
        var s = Regex.Replace(str.ToLowerInvariant(), @"[^a-z0-9\s]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var s = raw;

        // Remove copyright notices from OnePiecePower (e.g. "causa copyright oscurato")
        s = Regex.Replace(s, @"\bcausa\s+copyright\s+oscurato\b.*$", "", RegexOptions.IgnoreCase);

        // Strip bracketed content containing language/edition/genre metadata
        s = Regex.Replace(s, @"\[[^\]]*\]|\([^\)]*\)", m =>
        {
            var val = m.Value.ToLowerInvariant();
            if (val.Contains("sub") || val.Contains("ita") || val.Contains("dub") ||
                val.Contains("eng") || val.Contains("raw") || val.Contains("doppiat") ||
                val.Contains("film") || val.Contains("movie") || val.Contains("special") ||
                val.Contains("edition") || val.Contains("all") || val.Contains("shounen") ||
                val.Contains("seinen") || val.Contains("azione") || val.Contains("commedia") ||
                val.Contains("dramma") || val.Contains("fantasy") || val.Contains("romance"))
            {
                return " ";
            }
            return m.Value;
        });

        // Strip standalone language/format keywords
        s = Regex.Replace(s, @"\b(ita|sub|dub|subita|subeng|eng|english\s+edition|doppiato|special|specials|movie|film|ova|ona|oav|raw|one\s+shot)\b", " ", RegexOptions.IgnoreCase);

        // Strip symbols and normalize spaces
        s = Regex.Replace(s, @"[()\[\]/\\_:]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(s) ? raw.Trim() : s;
    }
}
