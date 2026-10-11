using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.Core.Models;
using GamingHub.Core.Services;
using System.IO;

namespace GamingHub.App.ViewModels;

public partial class GameItemViewModel : ViewModelBase
{
    private readonly GameLibraryService _library;

    public GameInfo Model { get; }

    public Avalonia.Media.Imaging.Bitmap? Icon => _icon;
    public Avalonia.Media.Imaging.Bitmap? Thumbnail => _thumbnail;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private TimeSpan _playtime;
    [ObservableProperty] private bool _isExpanded;

    public GameItemViewModel(GameInfo model, GameLibraryService library)
    {
        Model = model;
        _library = library;
        _sizeBytes = model.SizeOnDisk;
        _playtime = model.TotalPlaytime;
        _isRunning = model.IsRunning;

        // Load icon (small)
        if (!string.IsNullOrWhiteSpace(model.IconPath) && File.Exists(model.IconPath))
        {
            try { _icon = new Avalonia.Media.Imaging.Bitmap(model.IconPath); } catch { }
        }

        // Load thumbnail (larger, for the card)
        if (!string.IsNullOrWhiteSpace(model.ThumbnailPath) && File.Exists(model.ThumbnailPath))
        {
            try { _thumbnail = new Avalonia.Media.Imaging.Bitmap(model.ThumbnailPath); }
            catch { }
        }
        else if (!string.IsNullOrWhiteSpace(model.IconPath) && File.Exists(model.IconPath))
        {
            try { _thumbnail = new Avalonia.Media.Imaging.Bitmap(model.IconPath); } catch { }
        }
    }

    private Avalonia.Media.Imaging.Bitmap? _icon;
    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    public string Name => Model.Name;
    public string SourceName => Model.Source.ToString();
    public string InstallPath => Model.InstallPath ?? "—";
    public string SizeDisplay => SizeBytes > 0 ? Format.Bytes(SizeBytes) : "—";
    public string PlaytimeDisplay => Format.Playtime(Playtime);
    public string LastPlayedDisplay => Format.Date(Model.LastPlayedUtc);
    public bool CanLaunch => Model.CanLaunch;
    public bool SizeKnown => SizeBytes > 0;
    public string CategoryDisplay => GameClassifier.CategoryDisplayName(Model.Category);
    public bool IsGameCategory => Model.Category == GameCategory.Game;

    public string RunStateText => IsRunning ? "Läuft" : "Gestoppt";

    public bool HasThumbnail => Thumbnail != null;

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

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExpanded));
    }

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

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}
