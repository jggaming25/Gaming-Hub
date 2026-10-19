using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public sealed record CategoryFilterOption(string Label, GameCategory? Category);

/// <summary>Ein Laufwerks-Filter: <see cref="Label"/> wird angezeigt,
/// <see cref="Root"/> (z. B. "C:\") wird zum Vergleichen mit Installationspfaden genutzt.
/// Bei „Alle Laufwerke“ ist <see cref="Root"/> null.</summary>
public sealed class DriveFilterOption
{
    public string Label { get; init; } = string.Empty;
    public string? Root { get; init; }
    public override string ToString() => Label;
}

public partial class GamesViewModel : ViewModelBase
{
    private readonly List<GameItemViewModel> _all = new();
    private DispatcherTimer? _timer;

    public ObservableCollection<GameItemViewModel> Games { get; } = new();
    public ObservableCollection<DriveItemViewModel> Volumes { get; } = new();

    public ObservableCollection<CategoryFilterOption> CategoryOptions { get; } = new()
    {
        new("Alle Kategorien", null),
        new("Spiele", GameCategory.Game),
        new("Game-Tools", GameCategory.GameTool),
        new("Kommunikation", GameCategory.Communication),
        new("Office", GameCategory.Office),
        new("Entwicklung", GameCategory.Development),
        new("Zusätzliche Inhalte", GameCategory.GameContent),
        new("Browser", GameCategory.Browser),
        new("Kreativ & Streaming", GameCategory.Creator),
        new("Antivirus", GameCategory.Antivirus),
        new("KI-Tools", GameCategory.AiTool),
        new("System & Dienstprogramme", GameCategory.SystemService),
        new("Sonstige", GameCategory.Other)
    };

    public ObservableCollection<DriveFilterOption> DriveOptions { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private CategoryFilterOption _selectedCategory = null!;
    [ObservableProperty] private DriveFilterOption? _selectedDrive;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Bibliothek wird geladen…";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(CategoryFilterOption value) => ApplyFilter();
    partial void OnSelectedDriveChanged(DriveFilterOption? value) => ApplyFilter();

    public GamesViewModel()
    {
        SelectedCategory = CategoryOptions[0];
        LoadVolumes();
        StartTimer();

        // Bibliothek beim Start automatisch laden – Dank Cache ohne Voll-Rescan:
        // nur wenn sich echt etwas geändert hat, wird neu gescannt.
        _ = ScanAsync();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => RefreshStates();
        _timer.Start();
    }

    private bool _refreshingStates;
    private bool _computingSizes;

    private async void RefreshStates()
    {
        // UpdateRunningStates() liest für jeden Prozess den Pfad (MainModule) aus –
        // teuer, deshalb im Hintergrund; die UI-Aktualisierung erfolgt danach.
        if (_refreshingStates) return;
        _refreshingStates = true;
        try
        {
            await Task.Run(AppServices.Games.UpdateRunningStates);
            foreach (var g in _all)
                g.Refresh();
        }
        catch
        {
            // beim nächsten Tick erneut versuchen
        }
        finally
        {
            _refreshingStates = false;
        }
    }

    public void LoadVolumes()
    {
        Volumes.Clear();
        DriveOptions.Clear();

        var all = new DriveFilterOption { Label = "Alle Laufwerke", Root = null };
        DriveOptions.Add(all);
        foreach (var v in AppServices.DiskUsage.GetVolumes())
        {
            Volumes.Add(new DriveItemViewModel(v));
            DriveOptions.Add(new DriveFilterOption
            {
                Label = string.IsNullOrWhiteSpace(v.Label) ? v.Root : $"{v.Root} ({v.Label})",
                Root = v.Root
            });
        }

        if (SelectedDrive is null || !DriveOptions.Contains(SelectedDrive))
            SelectedDrive = all;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        bool usedCache = AppServices.Games.HasCache;
        StatusText = usedCache ? "Prüfe, ob sich deine Bibliothek geändert hat…" : "Scanne Spielbibliotheken (Steam, Epic, Registry)…";
        try
        {
            var games = await AppServices.Games.RefreshAsync();
            _all.Clear();
            foreach (var g in games)
                _all.Add(new GameItemViewModel(g, AppServices.Games, AppServices.Auth));
            ApplyFilter();
            StatusText = usedCache
                ? $"Bibliothek aus dem Cache geladen: {games.Count} Einträge (nur Änderungen werden angewendet)."
                : $"{games.Count} Einträge gefunden.";
            _ = ComputeMissingSizesAsync();
            _ = ResearchUnknownAsync();
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
    private async Task FullRescanAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Scanne komplett neu (ignoriert Cache)…";
        try
        {
            var games = await AppServices.Games.RefreshAsync(forceRescan: true);
            _all.Clear();
            foreach (var g in games)
                _all.Add(new GameItemViewModel(g, AppServices.Games, AppServices.Auth));
            ApplyFilter();
            StatusText = $"{games.Count} Einträge gefunden (vollständiger Scan).";
            _ = ComputeMissingSizesAsync();
            _ = ResearchUnknownAsync();
            if (games.Any(g => g.Category == GameCategory.Game))
                AppServices.Badges.Grant("bibliothekar");
        }
        catch (Exception ex)
        {
            StatusText = "Fehler beim vollständigen Scannen: " + ex.Message;
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

    /// <summary>Berechnet fehlende Ordnergrößen im Hintergrund, damit Karten
    /// direkt Größe und Pfad anzeigen können.</summary>
    private async Task ComputeMissingSizesAsync()
    {
        if (_computingSizes) return;
        _computingSizes = true;
        try
        {
            foreach (var g in _all.Where(x => x.SizeBytes == 0 && !string.IsNullOrWhiteSpace(x.Model.InstallPath)).ToList())
            {
                try
                {
                    g.SizeBytes = await Task.Run(() => AppServices.DiskUsage.GetDirectorySize(g.Model.InstallPath!));
                }
                catch
                {
                    // Einzelne Ordner dürfen den Rest nicht blockieren.
                }
            }
        }
        finally
        {
            _computingSizes = false;
        }
    }

    /// <summary>Lässt „Sonstige“-Einträge im Hintergrund per Web-Recherche
    /// einordnen und aktualisiert die Liste, sobald ein Spiel erkannt wurde.</summary>
    private async Task ResearchUnknownAsync()
    {
        try
        {
            int changed = await AppServices.Games.ResearchUnknownAsync();
            if (changed <= 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var game in _all)
                    game.Refresh();
                ApplyFilter();
                StatusText += $" · {changed} Sonstige automatisch als Spiel eingeordnet.";
            });
        }
        catch
        {
            // Ohne Internet bleibt die bisherige Einordnung bestehen.
        }
    }

    private void ApplyFilter()
    {
        Games.Clear();
        string q = SearchText?.Trim() ?? string.Empty;
        GameCategory? category = SelectedCategory?.Category;
        string? driveRoot = SelectedDrive?.Root;

        foreach (var g in _all)
        {
            if (q.Length > 0 && !g.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;
            if (category is { } cat && g.Model.Category != cat)
                continue;
            if (!string.IsNullOrWhiteSpace(driveRoot) &&
                !(g.Model.InstallPath?.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            Games.Add(g);
        }
    }
}