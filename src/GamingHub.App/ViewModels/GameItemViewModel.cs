using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public partial class GameItemViewModel : ViewModelBase
{
    private readonly GameLibraryService _library;

    public GameInfo Model { get; }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private TimeSpan _playtime;

    public GameItemViewModel(GameInfo model, GameLibraryService library)
    {
        Model = model;
        _library = library;
        _sizeBytes = model.SizeOnDisk;
        _playtime = model.TotalPlaytime;
        _isRunning = model.IsRunning;
    }

    public string Name => Model.Name;
    public string SourceName => Model.Source.ToString();
    public string InstallPath => Model.InstallPath ?? "—";
    public string SizeDisplay => SizeBytes > 0 ? Format.Bytes(SizeBytes) : "—";
    public string PlaytimeDisplay => Format.Playtime(Playtime);
    public string LastPlayedDisplay => Format.Date(Model.LastPlayedUtc);
    public bool CanLaunch => Model.CanLaunch;
    public bool SizeKnown => SizeBytes > 0;

    public string RunStateText => IsRunning ? "Läuft" : "Gestoppt";

    public void Refresh()
    {
        IsRunning = Model.IsRunning;
        Playtime = Model.TotalPlaytime;
        OnPropertyChanged(nameof(PlaytimeDisplay));
        OnPropertyChanged(nameof(LastPlayedDisplay));
        OnPropertyChanged(nameof(RunStateText));
    }

    partial void OnSizeBytesChanged(long value)
    {
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(SizeKnown));
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(RunStateText));

    partial void OnPlaytimeChanged(TimeSpan value) => OnPropertyChanged(nameof(PlaytimeDisplay));

    [RelayCommand]
    private void Launch()
    {
        _library.Launch(Model);
    }

    [RelayCommand]
    private void Stop()
    {
        _library.Stop(Model);
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        IsBusy = true;
        try { await _library.RestartAsync(Model); }
        finally { IsBusy = false; }
    }
}
