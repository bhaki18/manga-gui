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

    public string AudioBadge => Audio?.ToLower() switch
    {
        "ita" => "ITA",
        "subita" => "SUB ITA",
        "sub" => "SUB ITA",
        "both" => "ITA / SUB",
        _ => Type.ToUpper()
    };

    public string BadgeColor => Audio?.ToLower() switch
    {
        "ita" => "#10B981", // Emerald green
        "subita" => "#3B82F6", // Bright Blue
        "sub" => "#3B82F6",
        "both" => "#F59E0B", // Amber
        _ => "#8B5CF6" // Violet
    };

    public string SourceLabel => !string.IsNullOrWhiteSpace(SourceName) ? SourceName : Source;

    public string TypeIcon => Type.ToLower() == "anime" ? "🎬" : "📖";
}
