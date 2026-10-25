using System;
using System.Text.Json.Serialization;

namespace MangaGui.Models;

public class MediaItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "anime"; // "anime" or "manga"

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "it";

    [JsonPropertyName("audio")]
    public string? Audio { get; set; } // "ita", "subita", "both"

    [JsonPropertyName("genres")]
    public string? Genres { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("lastItem")]
    public string? LastItem { get; set; }

    [JsonPropertyName("lastAccessedAt")]
    public string? LastAccessedAt { get; set; }

    // Caption under the poster
    public string SubDubText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LastItem))
            {
                var label = Type.ToLower() == "manga" ? "Letto" : "Visto";
                var unit = Type.ToLower() == "manga" ? "cap." : "ep.";
                return $"{label}: {LastItem} ({Count} {unit})";
            }

            return Audio?.ToLower() switch
            {
                "both" => "Sub | Dub",
                "ita" => "Dubbed (ITA)",
                "subita" => "Subtitled (SUB ITA)",
                "sub" => "Subtitled",
                _ => Type.ToLower() == "manga" ? "Manga" : "Sub | Dub"
            };
        }
    }

    public bool HasImage => !string.IsNullOrWhiteSpace(Image);

    public string AudioBadge => Audio?.ToLower() switch
    {
        "ita" => "ITA",
        "subita" => "SUB ITA",
        "sub" => "SUB ITA",
        "both" => "SUB | DUB",
        _ => Type.ToUpper()
    };

    public string SourceLabel => !string.IsNullOrWhiteSpace(SourceName) ? SourceName : Source;

    public string TypeIcon => Type.ToLower() == "anime" ? "🎬" : "📖";
}
