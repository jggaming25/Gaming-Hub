using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

public partial class ProfileViewModel : ViewModelBase
{
    public ObservableCollection<Badge> Badges { get; } = new();

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public string VersionDisplay => AppInfo.VersionDisplay;
    public bool IsConfigured => AppServices.Auth.IsConfigured;
    public bool IsLoggedIn => AppServices.Auth.IsLoggedIn;

    public string DisplayName =>
        AppServices.Auth.CurrentUser?.DisplayName
        ?? AppServices.Auth.CurrentUser?.Email
        ?? "Gast (nicht angemeldet)";

    public string ConfigHint => IsConfigured
        ? string.Empty
        : "Supabase ist noch nicht konfiguriert. Trage unter Einstellungen die Projekt-URL und den anon-Key ein.";

    public ProfileViewModel()
    {
        AppServices.Auth.Changed += OnAuthChanged;
        RefreshBadges();
    }

    private void OnAuthChanged()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(DisplayName));
        StatusText = IsLoggedIn ? "Angemeldet." : "Abgemeldet.";
    }

    public void RefreshBadges()
    {
        Badges.Clear();
        foreach (var b in AppServices.Badges.GetStatus())
            Badges.Add(b);
    }

    [RelayCommand]
    private async Task SignInAsync() => await AuthActionAsync(true);

    [RelayCommand]
    private async Task SignUpAsync() => await AuthActionAsync(false);

    private async Task AuthActionAsync(bool signIn)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "Bitte E-Mail und Passwort eingeben.";
            return;
        }

        IsBusy = true;
        StatusText = signIn ? "Anmelden…" : "Registrieren…";
        try
        {
            var (ok, error) = signIn
                ? await AppServices.Auth.SignInAsync(Email, Password)
                : await AppServices.Auth.SignUpAsync(Email, Password);
            StatusText = ok
                ? "Erfolgreich angemeldet."
                : (signIn ? "Anmeldung fehlgeschlagen: " : "Registrierung fehlgeschlagen: ") + error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OAuthAsync(string provider)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Öffne {provider}-Anmeldung im Browser…";
        try
        {
            var (ok, error) = await AppServices.Auth.SignInWithOAuthAsync(provider);
            StatusText = ok ? "Erfolgreich angemeldet." : "Anmeldung fehlgeschlagen: " + error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await AppServices.Auth.SignOutAsync();
        StatusText = "Abgemeldet.";
    }
}
