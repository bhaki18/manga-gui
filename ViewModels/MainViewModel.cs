using System.Diagnostics;
using System.IO;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaGui.Models;
using MangaGui.Services;

namespace MangaGui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MangaCliService _cliService;

    [ObservableProperty]
    private string _activeTab = "Anime";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadProgressText = string.Empty;

    public string DownloadedTabColor => (ActiveTab == "Downloaded" || ActiveTab == "Downloads") ? "#FF640A" : "#D0D0D0";
    public bool IsDownloadedTabActive => ActiveTab == "Downloaded" || ActiveTab == "Downloads";
    public string DownloadIconText => IsDownloading ? "⏳" : "⬇";
    public string DownloadButtonText => IsDownloading ? (!string.IsNullOrWhiteSpace(DownloadProgressText) ? $"DOWNLOADING ({DownloadProgressText})" : "DOWNLOADING...") : "DOWNLOAD";


    [ObservableProperty]
    private MediaItem? _selectedItem;

    [ObservableProperty]
    private decimal _selectedNumber = 1;

    [ObservableProperty]
    private decimal _maxNumber = 1;

    [ObservableProperty]
    private string _selectedSource = "All sources";

    [ObservableProperty]
    private bool _isConfirmDialogVisible;

    [ObservableProperty]
    private string _confirmDialogTitle = "";

    [ObservableProperty]
    private string _confirmDialogMessage = "";

    private Func<Task>? _pendingConfirmAsyncAction;

    public string ActionButtonText => SelectedItem?.IsDownloaded == true
        ? (SelectedItem?.Type.ToLower() == "manga" ? "📖 READ OFFLINE" : "▶ PLAY OFFLINE")
        : (SelectedItem?.Type.ToLower() == "manga" ? "📖 READ NOW" : "▶ START WATCHING");
    public string NumberLabel => SelectedItem?.Type.ToLower() == "manga" ? "Chapter:" : "Episode:";

    public bool IsCurrentNumberWatched => SelectedItem != null && SelectedItem.WatchedNumbers != null && SelectedItem.WatchedNumbers.Contains((int)SelectedNumber);
    public string WatchedStatusBadge => IsCurrentNumberWatched ? (SelectedItem?.Type.ToLower() == "manga" ? "✔ READ" : "✔ WATCHED") : "";
    public string UnmarkButtonText => SelectedItem?.Type.ToLower() == "manga" ? "✕ Unmark as read" : "✕ Unmark as watched";

    public string AnimeTabColor => ActiveTab == "Anime" ? "#FF640A" : "#D0D0D0";
    public string MangaTabColor => ActiveTab == "Manga" ? "#FF640A" : "#D0D0D0";
    public string HistoryTabColor => (ActiveTab == "History" || ActiveTab == "Cronologia") ? "#FF640A" : "#D0D0D0";
    public bool IsHistoryTabActive => ActiveTab == "History" || ActiveTab == "Cronologia";

    public string PageHeaderTitle => ActiveTab switch
    {
        "Downloaded" or "Downloads" => "Downloaded",
        "History" or "Cronologia" => "History",
        "Anime" => "Most Popular Anime",
        "Manga" => "Most Popular Manga",
        _ => ActiveTab
    };

    public string PageSubHeader => ActiveTab switch
    {
        "Downloaded" or "Downloads" => "Installed Anime & Manga (.config/manga-gui/Downloaded)",
        "History" or "Cronologia" => "Recently Watched & Read",
        _ => "Popular"
    };

    public ObservableCollection<MediaItem> Items { get; } = new();

    public ObservableCollection<string> Sources { get; } = new()
    {
        "All sources",
        "animeworld",
        "onepiecepower",
        "onepiecepower-anime",
        "mangadex",
        "mangapill"
    };

    public MainViewModel(string[]? args = null)
    {
        _cliService = new MangaCliService();
        ParseCommandLineArgs(args);
        _ = LoadInitialCatalogAsync();
    }

    private void ParseCommandLineArgs(string[]? args)
    {
        if (args == null || args.Length == 0) return;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();
            if (arg == "--anime" || arg == "-a")
            {
                ActiveTab = "Anime";
            }
            else if (arg == "--manga" || arg == "-m")
            {
                ActiveTab = "Manga";
            }
            else if (arg == "--downloaded" || arg == "--downloads" || arg == "-d")
            {
                ActiveTab = "Downloaded";
            }
            else if (arg == "--history" || arg == "-h" || arg == "-H")
            {
                ActiveTab = "History";
            }
            else if ((arg == "--search" || arg == "-q") && i + 1 < args.Length)
            {
                SearchQuery = args[++i];
            }
            else if (arg == "--source" && i + 1 < args.Length)
            {
                SelectedSource = args[++i];
            }
            else if (!arg.StartsWith("-") && string.IsNullOrWhiteSpace(SearchQuery))
            {
                SearchQuery = args[i];
            }
        }
    }

    partial void OnSelectedSourceChanged(string value)
    {
        if (ActiveTab != "History" && ActiveTab != "Cronologia")
        {
            _ = LoadCatalogAsync();
        }
    }

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(AnimeTabColor));
        OnPropertyChanged(nameof(MangaTabColor));
        OnPropertyChanged(nameof(DownloadedTabColor));
        OnPropertyChanged(nameof(HistoryTabColor));
        OnPropertyChanged(nameof(PageHeaderTitle));
        OnPropertyChanged(nameof(PageSubHeader));
        OnPropertyChanged(nameof(IsHistoryTabActive));
        OnPropertyChanged(nameof(IsDownloadedTabActive));
    }

    partial void OnSelectedItemChanged(MediaItem? value)
    {
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(NumberLabel));
        OnPropertyChanged(nameof(IsCurrentNumberWatched));
        OnPropertyChanged(nameof(WatchedStatusBadge));
        OnPropertyChanged(nameof(UnmarkButtonText));
    }

    [RelayCommand]
    public void DecrementNumber()
    {
        if (SelectedNumber > 1)
        {
            SelectedNumber--;
        }
    }

    [RelayCommand]
    public void IncrementNumber()
    {
        if (SelectedNumber < MaxNumber)
        {
            SelectedNumber++;
        }
    }

    partial void OnSelectedNumberChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsCurrentNumberWatched));
        OnPropertyChanged(nameof(WatchedStatusBadge));
    }

    [RelayCommand]
    public async Task ShowDownloadedAsync()
    {
        ActiveTab = "Downloaded";
        SelectedItem = null;
        SearchQuery = string.Empty;
        IsLoading = true;
        StatusText = "Scanning installed files in ~/.config/manga-gui/Downloaded...";
        Items.Clear();

        try
        {
            var downloaded = await _cliService.GetDownloadedItemsAsync();
            foreach (var item in downloaded)
            {
                Items.Add(item);
            }
            StatusText = Items.Count == 0 
                ? "No downloaded anime or manga found in ~/.config/manga-gui/Downloaded." 
                : $"Downloaded: {Items.Count} titles installed offline.";
        }
        catch (Exception ex)
        {
            StatusText = $"Downloads error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenDownloadsFolder()
    {
        try
        {
            var baseDir = MangaCliService.GetBaseDownloadDirectory();
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            Process.Start(new ProcessStartInfo("xdg-open", baseDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DownloadCurrentItemAsync()
    {
        if (SelectedItem == null || IsDownloading) return;
        var num = (int)SelectedNumber;
        var title = SelectedItem.Title;
        var unit = SelectedItem.Type.ToLower() == "manga" ? "Chapter" : "Episode";

        IsDownloading = true;
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(DownloadIconText));
        OnPropertyChanged(nameof(DownloadButtonText));
        StatusText = $"Downloading {title} ({unit} {num})...";

        try
        {
            DownloadProgressText = string.Empty;
            var (success, msg, dest) = await _cliService.DownloadMediaAsync(SelectedItem, num, (string prog) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgressText = prog;
                    StatusText = $"Downloading {title} ({unit} {num}): {prog}";
                    OnPropertyChanged(nameof(DownloadButtonText));
                });
            });
            StatusText = msg;
            if (success && (ActiveTab == "Downloaded" || ActiveTab == "Downloads"))
            {
                await ShowDownloadedAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Download error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            DownloadProgressText = string.Empty;
            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(DownloadIconText));
            OnPropertyChanged(nameof(DownloadButtonText));
        }
    }

    [RelayCommand]
    public async Task ShowHistoryAsync()
    {
        ActiveTab = "History";
        SelectedItem = null;
        SearchQuery = string.Empty;
        IsLoading = true;
        StatusText = "Loading watch and read history...";
        Items.Clear();

        try
        {
            var history = await _cliService.GetHistoryAsync();
            foreach (var item in history)
            {
                Items.Add(item);
            }
            StatusText = $"History: {Items.Count} titles watched / read";
        }
        catch (Exception ex)
        {
            StatusText = $"History error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SwitchTab(string tabName)
    {
        if (ActiveTab == tabName) return;
        ActiveTab = tabName;
        SelectedItem = null;
        SearchQuery = string.Empty;

        if (tabName == "Downloaded" || tabName == "Downloads")
        {
            await ShowDownloadedAsync();
        }
        else if (tabName == "History" || tabName == "Cronologia")
        {
            await ShowHistoryAsync();
        }
        else
        {
            await LoadCatalogAsync();
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await LoadCatalogAsync();
            return;
        }

        IsLoading = true;
        StatusText = $"Searching for \"{SearchQuery}\"...";
        Items.Clear();

        try
        {
            var src = SelectedSource == "All sources" || SelectedSource == "Tutte le fonti" || SelectedSource == "all" ? null : SelectedSource;
            if (ActiveTab == "Anime")
            {
                var results = await _cliService.SearchAnimeAsync(SearchQuery, src);
                foreach (var item in results) Items.Add(item);
                StatusText = $"Found {Items.Count} anime for \"{SearchQuery}\"";
            }
            else
            {
                var results = await _cliService.SearchMangaAsync(SearchQuery, src);
                foreach (var item in results) Items.Add(item);
                StatusText = $"Found {Items.Count} manga for \"{SearchQuery}\"";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Search error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        IsLoading = true;
        StatusText = $"Loading {ActiveTab} catalog...";
        Items.Clear();

        try
        {
            var src = SelectedSource == "All sources" || SelectedSource == "Tutte le fonti" || SelectedSource == "all" ? null : SelectedSource;
            if (ActiveTab == "Anime")
            {
                var catalog = await _cliService.GetAnimeCatalogAsync(src);
                foreach (var item in catalog) Items.Add(item);
                StatusText = $"Anime Catalog: {Items.Count} titles ready";
            }
            else
            {
                var catalog = await _cliService.GetMangaCatalogAsync(src);
                foreach (var item in catalog) Items.Add(item);
                StatusText = $"Manga Catalog: {Items.Count} titles ready";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Catalog error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SelectItem(MediaItem item)
    {
        SelectedItem = item;
        var highestWatched = (item.WatchedNumbers != null && item.WatchedNumbers.Count > 0)
            ? item.WatchedNumbers.Max()
            : 1;

        MaxNumber = Math.Max(highestWatched, item.Count > 0 ? item.Count : 1);
        SelectedNumber = highestWatched;

        _ = LoadMediaDetailsAsync(item);
    }

    private async Task LoadMediaDetailsAsync(MediaItem item)
    {
        try
        {
            var total = await _cliService.GetMediaDetailsAsync(item);
            if (SelectedItem == item && total > 0)
            {
                MaxNumber = Math.Max((decimal)total, SelectedNumber);
            }
        }
        catch {}
    }

    [RelayCommand]
    public void CloseDetails()
    {
        SelectedItem = null;
    }

    [RelayCommand]
    public async Task UnmarkCurrentNumberAsync()
    {
        if (SelectedItem == null) return;
        var num = (int)SelectedNumber;
        var title = SelectedItem.Title;
        var unit = SelectedItem.Type.ToLower() == "manga" ? "Chapter" : "Episode";

        StatusText = $"Removing {unit} {num} from {title} history...";
        var success = await _cliService.UnmarkItemAsync(SelectedItem, num);
        if (success)
        {
            if (SelectedItem.WatchedNumbers != null)
            {
                SelectedItem.WatchedNumbers.Remove(num);
                SelectedItem.Count = SelectedItem.WatchedNumbers.Count;
                SelectedItem.UpdateWatchedSummary();
            }

            OnPropertyChanged(nameof(IsCurrentNumberWatched));
            OnPropertyChanged(nameof(WatchedStatusBadge));
            StatusText = $"✔ {unit} {num} removed from \"{title}\" history.";

            if ((ActiveTab == "History" || ActiveTab == "Cronologia") && SelectedItem.Count == 0)
            {
                Items.Remove(SelectedItem);
                SelectedItem = null;
            }
        }
        else
        {
            StatusText = $"Failed to remove {unit} {num} from history.";
        }
    }

    [RelayCommand]
    public void RequestClearAllHistory()
    {
        ConfirmDialogTitle = "Clear All History?";
        ConfirmDialogMessage = "Are you sure you want to permanently delete the ENTIRE history of watched anime and read manga? This action cannot be undone.";
        _pendingConfirmAsyncAction = async () =>
        {
            StatusText = "Clearing entire history...";
            await _cliService.ClearAllHistoryAsync();
            Items.Clear();
            SelectedItem = null;
            StatusText = "✔ All history has been successfully cleared.";
        };
        IsConfirmDialogVisible = true;
    }

    [RelayCommand]
    public void RequestRemoveItem()
    {
        if (SelectedItem == null) return;
        var title = SelectedItem.Title;

        if (SelectedItem.IsDownloaded)
        {
            ConfirmDialogTitle = "Delete Downloaded Title?";
            ConfirmDialogMessage = $"Are you sure you want to delete \"{title}\" and all its downloaded files from disk?";
            _pendingConfirmAsyncAction = async () =>
            {
                StatusText = $"Deleting \"{title}\" from disk...";
                var ok = await _cliService.DeleteDownloadedItemAsync(SelectedItem);
                if (ok)
                {
                    Items.Remove(SelectedItem);
                    SelectedItem = null;
                    StatusText = $"✔ Deleted \"{title}\" from disk.";
                }
                else
                {
                    StatusText = $"Could not delete \"{title}\".";
                }
            };
            IsConfirmDialogVisible = true;
            return;
        }
        ConfirmDialogTitle = "Remove from History?";
        ConfirmDialogMessage = $"Are you sure you want to remove \"{title}\" from history?";
        _pendingConfirmAsyncAction = async () =>
        {
            await RemoveItemFromHistoryAsync();
        };
        IsConfirmDialogVisible = true;
    }

    [RelayCommand]
    public async Task ConfirmDialogActionAsync()
    {
        IsConfirmDialogVisible = false;
        if (_pendingConfirmAsyncAction != null)
        {
            var action = _pendingConfirmAsyncAction;
            _pendingConfirmAsyncAction = null;
            await action();
        }
    }

    [RelayCommand]
    public void CancelDialogAction()
    {
        IsConfirmDialogVisible = false;
        _pendingConfirmAsyncAction = null;
    }

    [RelayCommand]
    public async Task RemoveItemFromHistoryAsync()
    {
        if (SelectedItem == null) return;
        var title = SelectedItem.Title;
        StatusText = $"Removing \"{title}\" from history...";
        var success = await _cliService.RemoveFromHistoryAsync(SelectedItem);
        if (success)
        {
            if (ActiveTab == "History" || ActiveTab == "Cronologia")
            {
                Items.Remove(SelectedItem);
            }
            SelectedItem = null;
            StatusText = $"✔ \"{title}\" completely removed from history.";
        }
        else
        {
            StatusText = $"Failed to remove \"{title}\" from history.";
        }
    }

    [RelayCommand]
    public async Task PlayOrReadAsync()
    {
        if (SelectedItem == null) return;
        var num = (int)SelectedNumber;

        if (SelectedItem.IsDownloaded && !string.IsNullOrWhiteSpace(SelectedItem.LocalPath))
        {
            try
            {
                if (SelectedItem.Type.ToLower() == "anime")
                {
                    var videoFiles = Directory.GetFiles(SelectedItem.LocalPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var targetFile = videoFiles.FirstOrDefault(f => f.Contains($"{num}")) ?? videoFiles.FirstOrDefault();
                    if (targetFile != null)
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = MangaCliService.ResolveMpvPath(),
                            Arguments = $"\"{targetFile}\" --title=\"{SelectedItem.Title} (Episode {num} - Offline)\"",
                            UseShellExecute = false
                        };
                        MangaCliService.PassDisplayEnv(psi);
                        Process.Start(psi);
                        StatusText = $"Playing offline: {SelectedItem.Title} ({Path.GetFileName(targetFile)})";
                        return;
                    }
                }
                else
                {
                    var subDirs = Directory.GetDirectories(SelectedItem.LocalPath);
                    var targetDir = subDirs.FirstOrDefault(d => d.Contains($"{num}")) ?? subDirs.FirstOrDefault() ?? SelectedItem.LocalPath;
                    var psi = new ProcessStartInfo
                    {
                        FileName = MangaCliService.ResolveMpvPath(),
                        Arguments = $"\"{targetDir}\" --image-display-duration=inf --title=\"{SelectedItem.Title} (Chapter {num} - Offline)\"",
                        UseShellExecute = false
                    };
                    MangaCliService.PassDisplayEnv(psi);
                    Process.Start(psi);
                    StatusText = $"Reading offline: {SelectedItem.Title} ({Path.GetFileName(targetDir)})";
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Offline playback error: {ex.Message}";
                return;
            }
        }
        if (SelectedItem.Type.ToLower() == "anime")
        {
            StatusText = $"Loading stream for {SelectedItem.Title} (Episode {num})...";
            var (success, msg) = await _cliService.PlayStreamAsync(SelectedItem, num);
            StatusText = msg;
        }
        else
        {
            StatusText = $"Loading pages for {SelectedItem.Title} (Chapter {num})...";
            var (success, msg) = await _cliService.ReadMangaAsync(SelectedItem, num);
            StatusText = msg;
        }
    }

    private async Task LoadInitialCatalogAsync()
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            await SearchAsync();
        }
        else if (ActiveTab == "Downloaded")
        {
            await ShowDownloadedAsync();
        }
        else if (ActiveTab == "History")
        {
            await ShowHistoryAsync();
        }
        else
        {
            await LoadCatalogAsync();
        }
    }
}
