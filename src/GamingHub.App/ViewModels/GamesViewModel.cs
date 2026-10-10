using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

public partial class GamesViewModel : ViewModelBase
{
    private readonly List<GameItemViewModel> _all = new();
    private readonly Dictionary<string, bool> _volumeUnit = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _timer;

    public ObservableCollection<GameItemViewModel> Games { get; } = new();
    public ObservableCollection<DriveItemViewModel> Volumes { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Klicke auf „Bibliothek scannen“, um deine Spiele zu finden.";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public GamesViewModel()
    {
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
        foreach (var v in AppServices.DiskUsage.GetVolumes())
            Volumes.Add(new DriveItemViewModel(v, _volumeUnit.TryGetValue(v.Root, out bool mb) && mb));
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
            StatusText = $"{games.Count} Spiele & Apps gefunden.";
            if (games.Count > 0) AppServices.Badges.Grant("bibliothekar");
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
        foreach (var g in _all)
        {
            if (q.Length == 0 || g.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                Games.Add(g);
        }
    }
}
