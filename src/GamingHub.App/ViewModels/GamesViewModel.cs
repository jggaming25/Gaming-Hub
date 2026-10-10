using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public sealed record CategoryFilterOption(string Label, GameCategory? Category);

public partial class GamesViewModel : ViewModelBase
{
    private readonly List<GameItemViewModel> _all = new();
    private DispatcherTimer? _timer;

    public ObservableCollection<GameItemViewModel> Games { get; } = new();
    public ObservableCollection<DriveItemViewModel> Volumes { get; } = new();

    public ObservableCollection<CategoryFilterOption> CategoryOptions { get; } = new()
    {
        new("Alle Kategorien", null),
        new("Spiele (nur Games)", GameCategory.Game),
        new("Zusätzliche Inhalte", GameCategory.GameContent),
        new("Game-Tools", GameCategory.GameTool),
        new("Office-Programme", GameCategory.Office),
        new("Kreativ & Streaming", GameCategory.Creator),
        new("Antivirus", GameCategory.Antivirus),
        new("KI-Tools", GameCategory.AiTool),
        new("System & Dienstprogramme", GameCategory.SystemService),
        new("Sonstige", GameCategory.Other)
    };

    public ObservableCollection<string> DriveOptions { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private CategoryFilterOption _selectedCategory = null!;
    [ObservableProperty] private string _selectedDrive = "Alle Laufwerke";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Klicke auf „Bibliothek scannen“, um deine Spiele zu finden.";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(CategoryFilterOption value) => ApplyFilter();
    partial void OnSelectedDriveChanged(string value) => ApplyFilter();

    public GamesViewModel()
    {
        SelectedCategory = CategoryOptions[0];
        LoadVolumes();
        StartTimer();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => RefreshStates();
        _timer.Start();
    }

    private void RefreshStates()
    {
        AppServices.Games.UpdateRunningStates();
        foreach (var g in _all)
            g.Refresh();
    }

    public void LoadVolumes()
    {
        Volumes.Clear();
        DriveOptions.Clear();
        DriveOptions.Add("Alle Laufwerke");
        foreach (var v in AppServices.DiskUsage.GetVolumes())
        {
            Volumes.Add(new DriveItemViewModel(v));
            DriveOptions.Add(string.IsNullOrWhiteSpace(v.Label) ? v.Root : $"{v.Root} ({v.Label})");
        }
        if (!DriveOptions.Contains(SelectedDrive))
            SelectedDrive = "Alle Laufwerke";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Scanne Spielbibliotheken (Steam, Epic, Registry)…";
        try
        {
            var games = await AppServices.Games.RefreshAsync();
            _all.Clear();
            foreach (var g in games)
                _all.Add(new GameItemViewModel(g, AppServices.Games));
            ApplyFilter();
            StatusText = $"{games.Count} Einträge gefunden.";
            if (games.Any(g => g.Category == GameCategory.Game))
                AppServices.Badges.Grant("bibliothekar");
        }
        catch (Exception ex)
        {
            StatusText = "Fehler beim Scannen: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearScan()
    {
        _all.Clear();
        Games.Clear();
        StatusText = "Scan gelöscht – klicke auf „Bibliothek scannen“, um erneut zu prüfen.";
    }

    [RelayCommand]
    private async Task LoadSizesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Berechne Speicherplatzbelegung…";
        try
        {
            foreach (var g in _all.Where(x => x.SizeBytes == 0 && !string.IsNullOrWhiteSpace(x.Model.InstallPath)))
            {
                long size = await Task.Run(() => AppServices.DiskUsage.GetDirectorySize(g.Model.InstallPath!));
                g.SizeBytes = size;
            }
            StatusText = "Speicherplatz berechnet.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RefreshVolumes() => LoadVolumes();

    private void ApplyFilter()
    {
        Games.Clear();
        string q = SearchText?.Trim() ?? string.Empty;
        GameCategory? category = SelectedCategory?.Category;
        string? drive = SelectedDrive;

        foreach (var g in _all)
        {
            if (q.Length > 0 && !g.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;
            if (category is { } cat && g.Model.Category != cat)
                continue;
            if (drive != "Alle Laufwerke" && drive is not null &&
                !(g.Model.InstallPath?.StartsWith(drive, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            Games.Add(g);
        }
    }
}