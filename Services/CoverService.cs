using System;
using System.Collections.Concurrent;
using System.IO;
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
    private static readonly string _cacheFile;

    static CoverService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json, application/json");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".local", "state", "manga-cli");
        Directory.CreateDirectory(dir);
        _cacheFile = Path.Combine(dir, "covers.json");
        LoadDiskCache();
    }

    private static void LoadDiskCache()
    {
        try
        {
            if (File.Exists(_cacheFile))
            {
                var json = File.ReadAllText(_cacheFile);
                var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
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
        catch {}
    }

    private static void SaveDiskCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_memoryCache);
            File.WriteAllText(_cacheFile, json);
        }
        catch {}
    }

    public static async Task<string?> GetCoverAsync(string title, string type = "anime")
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var clean = CleanTitle(title);
        var cacheKey = $"{type.ToLower()}:{clean.ToLower()}";

        if (_memoryCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        // 1. Try Kitsu first (fast, generous limits, high quality posters)
        var url = await QueryKitsuAsync(clean, type);

        // If not found and title has subtitle (e.g. "Series: Subtitle"), try base title
        if (string.IsNullOrWhiteSpace(url) && clean.Contains(':'))
        {
            var baseTitle = clean.Split(':')[0].Trim();
            if (baseTitle.Length > 2)
            {
                url = await QueryKitsuAsync(baseTitle, type);
            }
        }

        // 2. Try AniList as fallback
        if (string.IsNullOrWhiteSpace(url))
        {
            url = await QueryAniListAsync(clean, type);
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            _memoryCache[cacheKey] = url;
            SaveDiskCache();
            return url;
        }

        return null;
    }

    private static async Task<string?> QueryKitsuAsync(string cleanTitle, string type)
    {
        try
        {
            var endpoint = type.ToLower() == "manga" ? "manga" : "anime";
            var reqUrl = $"https://kitsu.io/api/edge/{endpoint}?filter[text]={Uri.EscapeDataString(cleanTitle)}&page[limit]=1";
            var res = await _httpClient.GetAsync(reqUrl);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.ValueKind == JsonValueKind.Array && 
                    data.GetArrayLength() > 0)
                {
                    var item = data[0];
                    if (item.TryGetProperty("attributes", out var attrs) &&
                        attrs.TryGetProperty("posterImage", out var poster) &&
                        poster.ValueKind == JsonValueKind.Object)
                    {
                        var posterUrl = poster.TryGetProperty("medium", out var m) ? m.GetString() :
                                        poster.TryGetProperty("small", out var s) ? s.GetString() :
                                        poster.TryGetProperty("original", out var o) ? o.GetString() : null;

                        if (!string.IsNullOrWhiteSpace(posterUrl))
                        {
                            return posterUrl;
                        }
                    }
                }
            }
        }
        catch {}

        return null;
    }

    private static async Task<string?> QueryAniListAsync(string cleanTitle, string type)
    {
        try
        {
            var mediaType = type.ToLower() == "manga" ? "MANGA" : "ANIME";
            var query = "query ($search: String, $type: MediaType) { Media(search: $search, type: $type) { coverImage { large medium } } }";
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
                    data.TryGetProperty("Media", out var media) &&
                    media.ValueKind != JsonValueKind.Null &&
                    media.TryGetProperty("coverImage", out var cover))
                {
                    return cover.TryGetProperty("large", out var l) ? l.GetString() :
                           cover.TryGetProperty("medium", out var m) ? m.GetString() : null;
                }
            }
        }
        catch {}

        return null;
    }

    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var s = Regex.Replace(raw, @"\b(ita|sub\s*ita|subita|english\s*sub|sub\s*eng|eng\s*sub|doppiato|special|movie|film|ova|ona)\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(ita/sub\s*ita|ita\s*/\s*sub\s*ita|sub\s*\|\s*dub)\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"[\(\)\[\]/]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(s) ? raw : s;
    }
}
