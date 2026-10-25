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
    private string _selectedSource = "all";

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
        SelectedNumber = 1;
    }

    [RelayCommand]
    public void CloseDetails()
    {
        SelectedItem = null;
    }

    [RelayCommand]
    public void PlayOrRead()
    {
        if (SelectedItem == null) return;

        if (SelectedItem.Type.ToLower() == "anime")
        {
            StatusText = $"Avvio riproduzione {SelectedItem.Title} (Episodio {SelectedNumber})...";
            _cliService.PlayStream(SelectedItem.Title, SelectedNumber, SelectedItem.Source);
        }
        else
        {
            StatusText = $"Apertura {SelectedItem.Title} (Capitolo {SelectedNumber})...";
            _cliService.ReadManga(SelectedItem.Title, SelectedNumber, SelectedItem.Source);
        }
    }

    private async Task LoadInitialCatalogAsync()
    {
        await LoadCatalogAsync();
    }
}
