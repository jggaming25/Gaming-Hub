using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
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

    [ObservableProperty] private string _premiumCode = string.Empty;
    [ObservableProperty] private string _premiumStatusText = "Kein aktives Premium.";
    [ObservableProperty] private string _premiumTimeRemaining = "–";
    [ObservableProperty] private bool _isPremiumActive;
    [ObservableProperty] private double _premiumProgress;
    [ObservableProperty] private string _premiumProgressText = string.Empty;

    public string VersionDisplay => AppInfo.VersionDisplay;
    public bool IsConfigured => AppServices.Auth.IsConfigured;
    public bool IsLoggedIn => AppServices.Auth.IsLoggedIn;

    private readonly DispatcherTimer _premiumTimer;

    public string DisplayName =>
        AppServices.Auth.CurrentUser?.DisplayName
        ?? AppServices.Auth.CurrentUser?.Email
        ?? "Gast (nicht angemeldet)";

    public string ConfigHint => IsConfigured
        ? string.Empty
        : "Supabase ist noch nicht konfiguriert. Trage unter Einstellungen die Projekt-URL und den anon-Key ein.";

    public ProfileViewModel()
    {
        _premiumTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _premiumTimer.Tick += (_, _) => UpdatePremiumCountdown();
        _premiumTimer.Start();

        AppServices.Auth.Changed += OnAuthChanged;
        AppServices.Premium.Changed += OnPremiumChanged;
        AppServices.Profile.Changed += OnProfileChanged;
        RefreshBadges();
        UpdatePremiumStatus();
    }

    private void OnProfileChanged()
    {
        RefreshBadges();
        UpdatePremiumStatus();
    }

    private void OnPremiumChanged()
    {
        UpdatePremiumStatus();
    }

    public void UpdatePremiumStatus()
    {
        var until = AppServices.Premium.PremiumUntil;
        IsPremiumActive = AppServices.Premium.IsPremium && !AppServices.Premium.IsPremiumBanned;

        if (AppServices.Premium.IsPremiumBanned)
            PremiumStatusText = "Premium wurde für dein Konto gesperrt.";
        else if (AppServices.Premium.IsPremium && until is { } u)
            PremiumStatusText = $"Premium aktiv bis {u.ToLocalTime():d}.";
        else if (until is { } expired)
            PremiumStatusText = $"Premium abgelaufen ({expired.ToLocalTime():d}).";
        else
            PremiumStatusText = "Kein aktives Premium.";

        UpdatePremiumCountdown();
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(IsPremiumBanned));
        OnPropertyChanged(nameof(PremiumUntilDisplay));
        OnPropertyChanged(nameof(IsPremiumActive));
    }

    private void UpdatePremiumCountdown()
    {
        var until = AppServices.Premium.PremiumUntil;
        if (until is { } u && u > DateTimeOffset.Now && IsPremiumActive)
        {
            var remaining = u - DateTimeOffset.Now;
            PremiumTimeRemaining = remaining.Days > 0
                ? $"{remaining.Days}d {remaining.Hours}h {remaining.Minutes}m"
                : $"{remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s";

            // Progress: assume max 365 days for progress bar
            var totalDays = 365.0;
            var elapsedDays = (DateTimeOffset.Now - (u - TimeSpan.FromDays(totalDays))).TotalDays;
            PremiumProgress = Math.Clamp((totalDays - remaining.TotalDays) / totalDays * 100, 0, 100);
            PremiumProgressText = $"{PremiumProgress:F1}% verbraucht";
        }
        else if (until is { } u2 && u2 == DateTimeOffset.MaxValue)
        {
            PremiumTimeRemaining = "Unbegrenzt";
            PremiumProgress = 0;
            PremiumProgressText = "Unbegrenzt";
        }
        else
        {
            PremiumTimeRemaining = IsPremiumActive ? "Unbegrenzt" : "–";
            PremiumProgress = 0;
            PremiumProgressText = string.Empty;
        }
    }

    public bool IsPremium => AppServices.Premium.IsPremium;

    public bool IsPremiumBanned => AppServices.Premium.IsPremiumBanned;

    public string PremiumUntilDisplay => AppServices.Premium.PremiumUntil is { } pu
        ? pu.ToLocalTime().ToString("d")
        : "–";

    private void OnAuthChanged()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(DisplayName));
        StatusText = IsLoggedIn ? "Angemeldet." : "Abgemeldet.";
        if (!IsLoggedIn)
        {
            Email = string.Empty;
            Password = string.Empty;
        }
        UpdatePremiumStatus();
    }

    [RelayCommand]
    private async Task RedeemPremiumAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        PremiumStatusText = "Code wird eingelöst…";
        try
        {
            var (ok, message, _) = await AppServices.Premium.RedeemAsync(PremiumCode);
            if (ok) PremiumCode = string.Empty;
            PremiumStatusText = message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RefreshBadges()
    {
        bool streak3 = AppServices.Profile.HasServerBadge("premium_streak3");
        Badges.Clear();
        foreach (var b in AppServices.Badges.GetStatus())
        {
            if (b.Id == "premium_streak3" && streak3)
                b.Earned = true;
            Badges.Add(b);
        }
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
