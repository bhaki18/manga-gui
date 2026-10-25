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

    public string ActionButtonText => SelectedItem?.Type.ToLower() == "manga" ? "📖 READ NOW" : "▶ START WATCHING";
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
        "History" or "Cronologia" => "History",
        "Anime" => "Most Popular Anime",
        "Manga" => "Most Popular Manga",
        _ => ActiveTab
    };

    public string PageSubHeader => ActiveTab switch
    {
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

    public MainViewModel()
    {
        _cliService = new MangaCliService();
        _ = LoadInitialCatalogAsync();
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
        OnPropertyChanged(nameof(HistoryTabColor));
        OnPropertyChanged(nameof(PageHeaderTitle));
        OnPropertyChanged(nameof(PageSubHeader));
        OnPropertyChanged(nameof(IsHistoryTabActive));
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
        await LoadCatalogAsync();
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
        await LoadCatalogAsync();
    }
}
