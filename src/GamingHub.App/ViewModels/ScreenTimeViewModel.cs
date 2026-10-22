using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

/// <summary>Eine App-Zeile in der Übersicht (Nutzung heute/Woche).</summary>
public sealed partial class ScreenTimeAppRow : ObservableObject
{
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    [ObservableProperty] private string _todayText = "–";
    [ObservableProperty] private string _weekText = "–";
    [ObservableProperty] private string _limitText = string.Empty;
}

/// <summary>Eine bearbeitbare Limit-Zeile. Änderungen werden sofort gespeichert.</summary>
public sealed partial class ScreenTimeLimitRow : ObservableObject
{
    private readonly AppLimit _model;
    private readonly Action _save;

    public string ProcessName => _model.ProcessName;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private int _dailyMinutes;
    [ObservableProperty] private int _weeklyMinutes;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _usageText = string.Empty;

    public ScreenTimeLimitRow(AppLimit model, Action save)
    {
        _model = model;
        _save = save;
        _displayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ProcessName : model.DisplayName;
        _dailyMinutes = model.DailyMinutes;
        _weeklyMinutes = model.WeeklyMinutes;
        _enabled = model.Enabled;
    }

    partial void OnDailyMinutesChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 24 * 60);
        if (value != clamped)
        {
            DailyMinutes = clamped;
            return;
        }
        _model.DailyMinutes = clamped;
        _save();
    }

    partial void OnWeeklyMinutesChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 7 * 24 * 60);
        if (value != clamped)
        {
            WeeklyMinutes = clamped;
            return;
        }
        _model.WeeklyMinutes = clamped;
        _save();
    }

    partial void OnEnabledChanged(bool value)
    {
        _model.Enabled = value;
        _save();
    }

    public void SetUsage(string text) => UsageText = text;
}

/// <summary>Bearbeitbare Regel für einen Wochentag.</summary>
public sealed partial class ScreenTimeWeekdayRow : ObservableObject
{
    private readonly ScreenTimeWeekdayRule _model;
    private readonly Action _save;

    public DayOfWeek Day => _model.Day;
    public string DayLabel => _model.Day switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        _ => "Sonntag"
    };

    [ObservableProperty] private string _allowedStart;
    [ObservableProperty] private string _allowedEnd;
    [ObservableProperty] private int _totalLimitMinutes;

    public ScreenTimeWeekdayRow(ScreenTimeWeekdayRule model, Action save)
    {
        _model = model;
        _save = save;
        _allowedStart = model.AllowedStart;
        _allowedEnd = model.AllowedEnd;
        _totalLimitMinutes = model.TotalLimitMinutes;
    }

    partial void OnAllowedStartChanged(string value)
    {
        _model.AllowedStart = value;
        _save();
    }

    partial void OnAllowedEndChanged(string value)
    {
        _model.AllowedEnd = value;
        _save();
    }

    partial void OnTotalLimitMinutesChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 24 * 60);
        if (value != clamped)
        {
            TotalLimitMinutes = clamped;
            return;
        }
        _model.TotalLimitMinutes = clamped;
        _save();
    }
}

/// <summary>Ein Balken im 4-Wochen-Diagramm der allgemeinen PC-Nutzung.</summary>
public sealed class ScreenTimeChartBar
{
    public string DateLabel { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public double BarHeight { get; init; }
    public bool IsToday { get; init; }
}

public partial class ScreenTimeViewModel : ViewModelBase
{
    private readonly DispatcherTimer _refreshTimer;
    private bool _suppressProfileWrite;

    public ObservableCollection<ScreenTimeAppRow> TopApps { get; } = new();
    public ObservableCollection<ScreenTimeLimitRow> Limits { get; } = new();
    public ObservableCollection<ScreenTimeWeekdayRow> Weekdays { get; } = new();
    public ObservableCollection<ScreenTimeChartBar> UsageChart { get; } = new();
    public ObservableCollection<ScreenTimeProfile> Profiles { get; } = new();
    public ObservableCollection<string> Processes { get; } = new();

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _enforce;
    [ObservableProperty] private string _todayTotalText = "0 min";
    [ObservableProperty] private string _todayDateText = string.Empty;
    [ObservableProperty] private string _activeProfileName = "Standard";
    [ObservableProperty] private string _newLimitProcess = string.Empty;
    [ObservableProperty] private string _newProfileName = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private ScreenTimeProfile? _selectedProfile;
    [ObservableProperty] private string _topAppName = "–";
    [ObservableProperty] private string _topAppText = "0 min";
    [ObservableProperty] private string _chartSummaryText = string.Empty;

    // Aktives Profil
    [ObservableProperty] private int _dailyLimitMinutes;
    [ObservableProperty] private bool _blockOutsideSchedule;
    [ObservableProperty] private string _weekdayStart = "07:00";
    [ObservableProperty] private string _weekdayEnd = "22:00";
    [ObservableProperty] private string _weekendStart = "08:00";
    [ObservableProperty] private string _weekendEnd = "23:00";

    public bool HasTopApps => TopApps.Count > 0;
    public bool HasLimits => Limits.Count > 0;

    /// <summary>Nutzung (Gesamt + pro App) ist für alle sichtbar; Limits nur mit Premium/Admin.</summary>
    public bool IsPremiumUser =>
        AppServices.Profile.IsAdmin ||
        (AppServices.Profile.IsPremium && !AppServices.Profile.PremiumBanned);

    public bool ShowLimitFeatures => IsPremiumUser;
    public bool ShowLimitHint => !IsPremiumUser;

    public string LimitHintText =>
        "Die Nutzung wird für alle erfasst. Limits, erlaubte Zeiten und Sperren sind mit " +
        "Gaming Hub Premium verfügbar.";

    public ScreenTimeViewModel()
    {
        AppServices.ScreenTime.EnsureProfile();
        Enabled = AppServices.ScreenTime.Enabled;
        Enforce = AppServices.ScreenTime.Enforce;

        RefreshProfileList();
        LoadActiveProfileIntoUi();
        LoadProcesses();

        AppServices.ScreenTime.Changed += OnServiceChanged;
        AppServices.Profile.Changed += () => Dispatcher.UIThread.Post(NotifyLimitAccessChanged);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshUsage();
        _refreshTimer.Start();
        RefreshUsage();
    }

    private void OnServiceChanged() => Dispatcher.UIThread.Post(() =>
    {
        RefreshProfileList();
        RefreshUsage();
    });

    private void NotifyLimitAccessChanged()
    {
        OnPropertyChanged(nameof(IsPremiumUser));
        OnPropertyChanged(nameof(ShowLimitFeatures));
        OnPropertyChanged(nameof(ShowLimitHint));
        RefreshUsage();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (AppServices.ScreenTime.Enabled != value)
            AppServices.ScreenTime.Enabled = value;
    }

    partial void OnEnforceChanged(bool value)
    {
        if (AppServices.ScreenTime.Enforce != value)
            AppServices.ScreenTime.Enforce = value;
    }

    private void RefreshProfileList()
    {
        string? selectedId = SelectedProfile?.Id;
        var profiles = AppServices.ScreenTime.Profiles;

        bool sameProfiles = Profiles.Count == profiles.Count &&
                            Profiles.Select(p => p.Id).SequenceEqual(profiles.Select(p => p.Id));
        if (!sameProfiles)
        {
            Profiles.Clear();
            foreach (var profile in profiles)
                Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId)
                          ?? Profiles.FirstOrDefault(p => p.Id == AppServices.ScreenTime.ActiveProfile?.Id);
    }

    private void LoadActiveProfileIntoUi()
    {
        var profile = AppServices.ScreenTime.ActiveProfile;
        if (profile is null) return;

        _suppressProfileWrite = true;
        ActiveProfileName = profile.Name;
        DailyLimitMinutes = profile.DailyLimitMinutes;
        BlockOutsideSchedule = profile.BlockOutsideSchedule;
        WeekdayStart = profile.WeekdayStart;
        WeekdayEnd = profile.WeekdayEnd;
        WeekendStart = profile.WeekendStart;
        WeekendEnd = profile.WeekendEnd;

        Limits.Clear();
        foreach (var limit in profile.AppLimits)
            Limits.Add(new ScreenTimeLimitRow(limit, SaveLimits));

        Weekdays.Clear();
        foreach (var rule in profile.Weekdays.OrderBy(r => ((int)r.Day + 6) % 7))
            Weekdays.Add(new ScreenTimeWeekdayRow(rule, SaveWeekdays));
        _suppressProfileWrite = false;

        OnPropertyChanged(nameof(HasLimits));
    }

    private void SaveWeekdays()
    {
        if (_suppressProfileWrite || !IsPremiumUser) return;
        AppServices.ScreenTime.UpdateActiveProfile(_ => { });
        RefreshUsage();
    }

    private void SaveLimits()
    {
        AppServices.ScreenTime.UpdateActiveProfile(_ => { });
        RefreshUsage();
    }

    private void SaveProfileSettings()
    {
        if (_suppressProfileWrite || !IsPremiumUser) return;
        AppServices.ScreenTime.UpdateActiveProfile(profile =>
        {
            profile.DailyLimitMinutes = Math.Clamp(DailyLimitMinutes, 0, 24 * 60);
            profile.BlockOutsideSchedule = BlockOutsideSchedule;
            profile.WeekdayStart = WeekdayStart;
            profile.WeekdayEnd = WeekdayEnd;
            profile.WeekendStart = WeekendStart;
            profile.WeekendEnd = WeekendEnd;
        });
        ActiveProfileName = AppServices.ScreenTime.ActiveProfile?.Name ?? ActiveProfileName;
    }

    partial void OnDailyLimitMinutesChanged(int value) => SaveProfileSettings();
    partial void OnBlockOutsideScheduleChanged(bool value) => SaveProfileSettings();
    partial void OnWeekdayStartChanged(string value) => SaveProfileSettings();
    partial void OnWeekdayEndChanged(string value) => SaveProfileSettings();
    partial void OnWeekendStartChanged(string value) => SaveProfileSettings();
    partial void OnWeekendEndChanged(string value) => SaveProfileSettings();

    public void RefreshUsage()
    {
        var service = AppServices.ScreenTime;
        TodayTotalText = FormatDuration(service.TodayTotalSeconds);
        TodayDateText = DateTime.Now.ToString("dddd, dd.MM.yyyy");
        ActiveProfileName = service.ActiveProfileName;

        TopApps.Clear();
        foreach (var (process, display, seconds) in service.GetTopApps())
        {
            int week = service.GetWeekSeconds(process);
            var limit = service.ActiveProfile?.AppLimits.FirstOrDefault(a =>
                string.Equals(a.ProcessName, process, StringComparison.OrdinalIgnoreCase));
            string limitText = limit is null || limit.DailyMinutes <= 0
                ? string.Empty
                : $"Limit: {limit.DailyMinutes} min/Tag";
            TopApps.Add(new ScreenTimeAppRow
            {
                ProcessName = process,
                DisplayName = display,
                TodayText = FormatDuration(seconds),
                WeekText = FormatDuration(week),
                LimitText = limitText
            });
        }
        OnPropertyChanged(nameof(HasTopApps));

        var top = TopApps.FirstOrDefault();
        TopAppName = top?.DisplayName ?? "–";
        TopAppText = top?.TodayText ?? "0 min";

        foreach (var row in Limits)
        {
            int today = service.GetTodaySeconds(row.ProcessName);
            int week = service.GetWeekSeconds(row.ProcessName);
            row.SetUsage($"Heute {FormatDuration(today)} · Woche {FormatDuration(week)}");
        }

        RefreshUsageChart(service);
    }

    private void RefreshUsageChart(ScreenTimeService service)
    {
        var totals = service.GetDailyTotals(28);
        int maxSeconds = Math.Max(60, totals.Count == 0 ? 0 : totals.Max(t => t.Seconds));
        int sumSeconds = totals.Sum(t => t.Seconds);

        UsageChart.Clear();
        for (int i = 0; i < totals.Count; i++)
        {
            var (date, seconds) = totals[i];
            double height = seconds <= 0 ? 2 : Math.Max(4, 92.0 * seconds / maxSeconds);
            UsageChart.Add(new ScreenTimeChartBar
            {
                DateLabel = date.ToString("dd.MM"),
                ValueText = $"{date:dd.MM.yyyy}: {FormatDuration(seconds)}",
                BarHeight = height,
                IsToday = date.Date == DateTime.Today
            });
        }

        ChartSummaryText = totals.Count == 0
            ? "Noch keine Nutzungsdaten."
            : $"Letzte 4 Wochen: {FormatDuration(sumSeconds)} gesamt · Ø {FormatDuration(sumSeconds / totals.Count)} pro Tag";
    }

    private static string FormatDuration(int totalSeconds)
    {
        if (totalSeconds <= 0) return "0 min";
        if (totalSeconds < 60) return "<1 min";
        return $"{Math.Round(totalSeconds / 60.0):0} min";
    }

    private void LoadProcesses()
    {
        Processes.Clear();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    string name = process.ProcessName;
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
                catch { /* Zugriff verweigert */ }
                finally { process.Dispose(); }
            }
        }
        catch { /* ignore */ }

        foreach (var name in names)
            Processes.Add(name);
    }

    [RelayCommand]
    private void AddLimit()
    {
        if (!IsPremiumUser) return;
        string process = NewLimitProcess.Trim();
        if (string.IsNullOrWhiteSpace(process))
        {
            StatusText = "Bitte eine App auswählen oder eingeben.";
            return;
        }

        AppServices.ScreenTime.AddAppLimit(process, AppServices.ScreenTime.GetDisplayName(process));
        NewLimitProcess = string.Empty;
        StatusText = $"Limit für „{process}“ hinzugefügt.";
        LoadActiveProfileIntoUi();
        RefreshUsage();
    }

    [RelayCommand]
    private void RemoveLimit(ScreenTimeLimitRow? row)
    {
        if (!IsPremiumUser || row is null) return;
        AppServices.ScreenTime.RemoveAppLimit(row.ProcessName);
        PropertyChangedForRows();
        LoadActiveProfileIntoUi();
        RefreshUsage();
    }

    private void PropertyChangedForRows()
    {
        OnPropertyChanged(nameof(HasLimits));
        OnPropertyChanged(nameof(ActiveProfileName));
    }

    [RelayCommand]
    private void ApplyProfile()
    {
        if (!IsPremiumUser || SelectedProfile is null) return;
        AppServices.ScreenTime.ApplyProfile(SelectedProfile.Id);
        LoadActiveProfileIntoUi();
        RefreshUsage();
        StatusText = $"Profil „{SelectedProfile.Name}“ angewendet.";
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (!IsPremiumUser) return;
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            StatusText = "Bitte einen Profilnamen eingeben.";
            return;
        }

        AppServices.ScreenTime.SaveProfile(NewProfileName);
        NewProfileName = string.Empty;
        RefreshProfileList();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == AppServices.ScreenTime.ActiveProfile?.Id);
        LoadActiveProfileIntoUi();
        StatusText = "Profil gespeichert und aktiviert.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (!IsPremiumUser || SelectedProfile is null) return;
        if (Profiles.Count <= 1)
        {
            StatusText = "Mindestens ein Profil muss erhalten bleiben.";
            return;
        }

        AppServices.ScreenTime.DeleteProfile(SelectedProfile.Id);
        RefreshProfileList();
        LoadActiveProfileIntoUi();
        StatusText = "Profil gelöscht.";
    }

    [RelayCommand]
    private void ReloadProcesses() => LoadProcesses();

    public void Dispose()
    {
        _refreshTimer.Stop();
        AppServices.ScreenTime.Changed -= OnServiceChanged;
    }
}
