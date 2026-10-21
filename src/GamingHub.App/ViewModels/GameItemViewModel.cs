using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.Core.Models;
using GamingHub.Core.Services;
using GamingHub.App.Services;
using System.IO;
using Avalonia.Controls;

namespace GamingHub.App.ViewModels;

public partial class GameItemViewModel : ViewModelBase
{
    private readonly GameLibraryService _library;
    private readonly AuthService _auth;

    public GameInfo Model { get; }

    public Avalonia.Media.Imaging.Bitmap? Icon => _icon;
    public Avalonia.Media.Imaging.Bitmap? Thumbnail => _thumbnail;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private TimeSpan _playtime;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isPasswordProtected;
    [ObservableProperty] private bool _isPasswordPromptVisible;
    [ObservableProperty] private string _passwordInput = string.Empty;
    [ObservableProperty] private string _passwordError = string.Empty;

    public GameItemViewModel(GameInfo model, GameLibraryService library, AuthService auth)
    {
        Model = model;
        _library = library;
        _auth = auth;
        _sizeBytes = model.SizeOnDisk;
        _playtime = model.TotalPlaytime;
        _isRunning = model.IsRunning;
        _isPasswordProtected = model.IsPasswordProtected;

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
    public string SourceName => Model.Source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic Games",
        GameSource.Registry => "Installiert",
        GameSource.Manual => "Manuell",
        _ => Model.Source.ToString()
    };
    public string InstallPath => Model.InstallPath ?? string.Empty;
    public string PathDisplay => !string.IsNullOrWhiteSpace(Model.ExecutablePath)
        ? Model.ExecutablePath!
        : Model.InstallPath ?? string.Empty;
    public string ExecutableDisplay => Model.ExecutablePath ?? string.Empty;
    public string FolderDisplay => Model.InstallPath ?? string.Empty;
    public bool HasExecutable => !string.IsNullOrWhiteSpace(Model.ExecutablePath);
    public bool HasFolder => !string.IsNullOrWhiteSpace(Model.InstallPath);
    public bool HasPath => HasExecutable || HasFolder;
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
        OnPropertyChanged(nameof(CategoryDisplay));
        OnPropertyChanged(nameof(IsGameCategory));
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

    partial void OnIsPasswordProtectedChanged(bool value)
    {
        Model.IsPasswordProtected = value;
        AppServices.Settings.Save();
    }

    partial void OnIsPasswordPromptVisibleChanged(bool value)
    {
        if (!value)
        {
            PasswordInput = string.Empty;
            PasswordError = string.Empty;
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (Model.IsPasswordProtected)
        {
            // Show password prompt
            PasswordInput = string.Empty;
            PasswordError = string.Empty;
            IsPasswordPromptVisible = true;
            return;
        }

        await ExecuteLaunchAsync(null);
    }

    [RelayCommand]
    private async Task ConfirmPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(PasswordInput))
        {
            PasswordError = "Bitte Passwort eingeben.";
            return;
        }

        var password = PasswordInput;
        IsPasswordPromptVisible = false;
        PasswordInput = string.Empty;

        await ExecuteLaunchAsync(password);
    }

    [RelayCommand]
    private void CancelPasswordPrompt()
    {
        IsPasswordPromptVisible = false;
        PasswordInput = string.Empty;
        PasswordError = string.Empty;
    }

    private async Task ExecuteLaunchAsync(string? password)
    {
        if (Model.IsPasswordProtected)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                IsPasswordPromptVisible = true;
                PasswordError = "Passwort erforderlich.";
                return;
            }

            // Verify password via Supabase
            if (_auth?.Client?.Session?.User?.Email is not { } email)
            {
                PasswordError = "Nicht angemeldet.";
                IsPasswordPromptVisible = true;
                return;
            }

            var (ok, _) = await _auth.SignInAsync(_auth.Client.Session.User.Email, password);
            if (!ok)
            {
                PasswordError = "Falsches Passwort.";
                IsPasswordPromptVisible = true;
                return;
            }
        }

        var success = _library.Launch(Model);
        
        if (!success && Model.IsPasswordProtected)
        {
            IsPasswordPromptVisible = true;
            PasswordError = "Falsches Passwort oder Anmeldung erforderlich.";
        }
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