using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MangaGui.Models;

public partial class MediaItem : ObservableObject
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [property: JsonPropertyName("image")]
    private string? _image;

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

    [ObservableProperty]
    [property: JsonPropertyName("count")]
    private int _count;

    [ObservableProperty]
    [property: JsonPropertyName("lastItem")]
    private string? _lastItem;

    [JsonPropertyName("lastAccessedAt")]
    public string? LastAccessedAt { get; set; }

    [ObservableProperty]
    [property: JsonPropertyName("watchedSummary")]
    private string? _watchedSummary;

    [JsonPropertyName("watchedNumbers")]
    public List<int>? WatchedNumbers { get; set; } = new();

    public void UpdateWatchedSummary()
    {
        if (WatchedNumbers == null || WatchedNumbers.Count == 0)
        {
            WatchedSummary = null;
            LastItem = null;
            OnPropertyChanged(nameof(SubDubText));
            return;
        }

        var sorted = WatchedNumbers.Distinct().OrderBy(n => n).ToList();
        var ranges = new List<string>();
        int start = sorted[0];
        int prev = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            int cur = sorted[i];
            if (cur == prev + 1)
            {
                prev = cur;
            }
            else
            {
                ranges.Add(start == prev ? $"{start}" : $"{start}-{prev}");
                start = cur;
                prev = cur;
            }
        }
        ranges.Add(start == prev ? $"{start}" : $"{start}-{prev}");

        var prefix = Type.ToLower() == "manga" ? "Cap." : "Ep.";
        WatchedSummary = $"{prefix} {string.Join(", ", ranges)}";
        LastItem = $"{prefix} {sorted.Last()}";
        OnPropertyChanged(nameof(SubDubText));
    }

    // Caption under the poster
    public string SubDubText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(WatchedSummary))
            {
                var label = Type.ToLower() == "manga" ? "Letti" : "Visti";
                var unit = Type.ToLower() == "manga" ? "cap." : "ep.";
                return $"{label}: {WatchedSummary} ({Count} {unit})";
            }

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
