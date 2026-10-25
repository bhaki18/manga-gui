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
    private string _statusText = "Pronto";

    [ObservableProperty]
    private MediaItem? _selectedItem;

    [ObservableProperty]
    private decimal _selectedNumber = 1;

    [ObservableProperty]
    private decimal _maxNumber = 1;

    [ObservableProperty]
    private string _selectedSource = "Tutte le fonti";

    public string ActionButtonText => SelectedItem?.Type.ToLower() == "manga" ? "📖 LEGGI ORA" : "▶ INIZIA A GUARDARE";
    public string NumberLabel => SelectedItem?.Type.ToLower() == "manga" ? "Capitolo:" : "Episodio:";

    public bool IsCurrentNumberWatched => SelectedItem != null && SelectedItem.WatchedNumbers != null && SelectedItem.WatchedNumbers.Contains((int)SelectedNumber);
    public string WatchedStatusBadge => IsCurrentNumberWatched ? (SelectedItem?.Type.ToLower() == "manga" ? "✔ LETTO" : "✔ VISTO") : "";
    public string UnmarkButtonText => SelectedItem?.Type.ToLower() == "manga" ? "✕ Deseleziona dai letti" : "✕ Deseleziona dai visti";

    public string AnimeTabColor => ActiveTab == "Anime" ? "#FF640A" : "#D0D0D0";
    public string MangaTabColor => ActiveTab == "Manga" ? "#FF640A" : "#D0D0D0";
    public string HistoryTabColor => ActiveTab == "Cronologia" ? "#FF640A" : "#D0D0D0";

    public string PageHeaderTitle => ActiveTab switch
    {
        "Cronologia" => "Cronologia",
        "Anime" => "Most Popular Anime",
        "Manga" => "Most Popular Manga",
        _ => ActiveTab
    };

    public string PageSubHeader => ActiveTab switch
    {
        "Cronologia" => "Opere Guardate e Lette di Recente",
        _ => "Popular"
    };

    public ObservableCollection<MediaItem> Items { get; } = new();

    public ObservableCollection<string> Sources { get; } = new()
    {
        "Tutte le fonti",
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
        if (ActiveTab != "Cronologia")
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
    }

    partial void OnSelectedItemChanged(MediaItem? value)
    {
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(NumberLabel));
        OnPropertyChanged(nameof(IsCurrentNumberWatched));
        OnPropertyChanged(nameof(WatchedStatusBadge));
        OnPropertyChanged(nameof(UnmarkButtonText));
    }

    partial void OnSelectedNumberChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsCurrentNumberWatched));
        OnPropertyChanged(nameof(WatchedStatusBadge));
    }

    [RelayCommand]
    public async Task ShowHistoryAsync()
    {
        ActiveTab = "Cronologia";
        SelectedItem = null;
        SearchQuery = string.Empty;
        IsLoading = true;
        StatusText = "Caricamento anime e manga guardati...";
        Items.Clear();

        try
        {
            var history = await _cliService.GetHistoryAsync();
            foreach (var item in history)
            {
                Items.Add(item);
            }
            StatusText = $"Cronologia: {Items.Count} titoli guardati / letti";
        }
        catch (Exception ex)
        {
            StatusText = $"Errore cronologia: {ex.Message}";
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
        StatusText = $"Ricerca in corso per \"{SearchQuery}\"...";
        Items.Clear();

        try
        {
            var src = SelectedSource == "Tutte le fonti" || SelectedSource == "all" ? null : SelectedSource;
            if (ActiveTab == "Anime")
            {
                var results = await _cliService.SearchAnimeAsync(SearchQuery, src);
                foreach (var item in results) Items.Add(item);
                StatusText = $"Trovati {Items.Count} anime per \"{SearchQuery}\"";
            }
            else
            {
                var results = await _cliService.SearchMangaAsync(SearchQuery, src);
                foreach (var item in results) Items.Add(item);
                StatusText = $"Trovati {Items.Count} manga per \"{SearchQuery}\"";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Errore ricerca: {ex.Message}";
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
        StatusText = $"Caricamento catalogo {ActiveTab}...";
        Items.Clear();

        try
        {
            var src = SelectedSource == "Tutte le fonti" || SelectedSource == "all" ? null : SelectedSource;
            if (ActiveTab == "Anime")
            {
                var catalog = await _cliService.GetAnimeCatalogAsync(src);
                foreach (var item in catalog) Items.Add(item);
                StatusText = $"Catalogo Anime: {Items.Count} titoli pronti";
            }
            else
            {
                var catalog = await _cliService.GetMangaCatalogAsync(src);
                foreach (var item in catalog) Items.Add(item);
                StatusText = $"Catalogo Manga: {Items.Count} titoli pronti";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Errore catalogo: {ex.Message}";
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
        var unit = SelectedItem.Type.ToLower() == "manga" ? "Capitolo" : "Episodio";

        StatusText = $"Rimozione {unit} {num} dalla cronologia di {title}...";
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
            StatusText = $"✔ {unit} {num} rimosso dalla cronologia di \"{title}\".";

            if (ActiveTab == "Cronologia" && SelectedItem.Count == 0)
            {
                Items.Remove(SelectedItem);
                SelectedItem = null;
            }
        }
        else
        {
            StatusText = $"Impossibile rimuovere {unit} {num} dalla cronologia.";
        }
    }

    [RelayCommand]
    public async Task RemoveItemFromHistoryAsync()
    {
        if (SelectedItem == null) return;
        var title = SelectedItem.Title;
        StatusText = $"Rimozione \"{title}\" dalla cronologia...";
        var success = await _cliService.RemoveFromHistoryAsync(SelectedItem);
        if (success)
        {
            if (ActiveTab == "Cronologia")
            {
                Items.Remove(SelectedItem);
            }
            SelectedItem = null;
            StatusText = $"✔ \"{title}\" rimosso completamente dalla cronologia.";
        }
        else
        {
            StatusText = $"Impossibile rimuovere \"{title}\" dalla cronologia.";
        }
    }

    [RelayCommand]
    public async Task PlayOrReadAsync()
    {
        if (SelectedItem == null) return;

        var num = (int)SelectedNumber;
        if (SelectedItem.Type.ToLower() == "anime")
        {
            StatusText = $"Caricamento flusso per {SelectedItem.Title} (Episodio {num})...";
            var (success, msg) = await _cliService.PlayStreamAsync(SelectedItem, num);
            StatusText = msg;
        }
        else
        {
            StatusText = $"Caricamento pagine per {SelectedItem.Title} (Capitolo {num})...";
            var (success, msg) = await _cliService.ReadMangaAsync(SelectedItem, num);
            StatusText = msg;
        }
    }

    private async Task LoadInitialCatalogAsync()
    {
        await LoadCatalogAsync();
    }
}
