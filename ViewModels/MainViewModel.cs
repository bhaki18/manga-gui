using System;
using System.Collections.ObjectModel;
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
    private int _selectedNumber = 1;

    [ObservableProperty]
    private int _maxNumber = 1;

    [ObservableProperty]
    private string _selectedSource = "all";

    public string ActionButtonText => SelectedItem?.Type.ToLower() == "manga" ? "📖 LEGGI ORA" : "▶ INIZIA A GUARDARE";
    public string NumberLabel => SelectedItem?.Type.ToLower() == "manga" ? "Capitolo:" : "Episodio:";

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
        MaxNumber = item.Count > 0 ? item.Count : 1;
        SelectedNumber = item.Count > 0 ? Math.Min(item.Count, MaxNumber) : 1;
        _ = LoadMediaDetailsAsync(item);
    }

    private async Task LoadMediaDetailsAsync(MediaItem item)
    {
        try
        {
            var total = await _cliService.GetMediaDetailsAsync(item);
            if (SelectedItem == item && total > 0)
            {
                MaxNumber = total;
                if (SelectedNumber > MaxNumber)
                {
                    SelectedNumber = MaxNumber;
                }
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
    public async Task PlayOrReadAsync()
    {
        if (SelectedItem == null) return;

        if (SelectedItem.Type.ToLower() == "anime")
        {
            StatusText = $"Caricamento flusso per {SelectedItem.Title} (Episodio {SelectedNumber})...";
            var (success, msg) = await _cliService.PlayStreamAsync(SelectedItem, SelectedNumber);
            StatusText = msg;
        }
        else
        {
            StatusText = $"Caricamento pagine per {SelectedItem.Title} (Capitolo {SelectedNumber})...";
            var (success, msg) = await _cliService.ReadMangaAsync(SelectedItem, SelectedNumber);
            StatusText = msg;
        }
    }

    private async Task LoadInitialCatalogAsync()
    {
        await LoadCatalogAsync();
    }
}
