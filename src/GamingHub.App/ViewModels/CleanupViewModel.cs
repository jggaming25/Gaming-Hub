using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public partial class CleanupViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;

    // Automatische Mindestgröße – kleine Dateien werden übersprungen, damit der
    // Scan schnell bleibt. Die frühere manuelle 1/10/100/1024-MB-Auswahl entfällt.
    private const long AutoMinFileSizeBytes = 64 * 1024;

    public ObservableCollection<string> ScanPaths { get; } = new();
    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();
    public ObservableCollection<DriveScanOption> Drives { get; } = new();

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Wähle Ordner und starte die Prüfung auf doppelte Dateien.";
    [ObservableProperty] private string _progressPhase = string.Empty;
    [ObservableProperty] private int _scanPercent;
    [ObservableProperty] private string _elapsedDisplay = string.Empty;
    [ObservableProperty] private int _groupsCount;
    [ObservableProperty] private string _wastedDisplay = "0 B";
    [ObservableProperty] private string _etaDisplay = string.Empty;

    private double _lastFraction = -1;
    private DateTime _lastFractionAtUtc;
    private readonly DispatcherTimer _etaTimer;

    public bool UseRecycleBin
    {
        get => AppServices.Settings.Settings.UseRecycleBin;
        set
        {
            AppServices.Settings.Settings.UseRecycleBin = value;
            AppServices.Settings.Save();
            OnPropertyChanged();
        }
    }

    public CleanupViewModel()
    {
        LoadDrives();

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (_, _) =>
        {
            if (IsScanning)
                ElapsedDisplay = Format.Elapsed(_stopwatch.Elapsed);
        };
        _elapsedTimer.Start();

        // ETA-Timer: alle 10 Sekunden neu berechnen, damit die Anzeige
        // auch bei Stillstand des Fortschritts (z. B. große Datei) aktualisiert wird.
        _etaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _etaTimer.Tick += (_, _) =>
        {
            if (IsScanning && _lastFraction >= 0)
            {
                var now = DateTime.UtcNow;
                double seconds = (now - _lastFractionAtUtc).TotalSeconds;
                if (seconds >= 5) // nur aktualisieren, wenn letzter Messpunkt alt genug
                    UpdateEta(_lastFraction);
            }
        };
        _etaTimer.Start();

        var settings = AppServices.Settings.Settings;
        if (settings.CleanupScanPaths.Count > 0)
        {
            foreach (var p in settings.CleanupScanPaths)
                if (Directory.Exists(p))
                    ScanPaths.Add(p);
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] defaults =
            {
                Path.Combine(home, "Downloads"),
                Path.Combine(home, "Desktop"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            foreach (string p in defaults)
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p) && !ScanPaths.Contains(p))
                    ScanPaths.Add(p);
        }
    }

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var v in AppServices.DiskUsage.GetVolumes())
            Drives.Add(new DriveScanOption(v.Root, v.Label));
    }

    [RelayCommand]
    private void AddSelectedDrives()
    {
        int added = 0;
        foreach (var d in Drives)
        {
            if (d.IsSelected && !ScanPaths.Contains(d.Root))
            {
                ScanPaths.Add(d.Root);
                added++;
            }
        }
        if (added > 0)
        {
            PersistPaths();
            StatusText = $"{added} Laufwerk(e) zur Prüfung hinzugefügt.";
        }
    }

    [RelayCommand]
    private void ClearScan()
    {
        Groups.Clear();
        UpdateStats();
        StatusText = "Scan gelöscht. Wähle Ordner oder Laufwerke und starte erneut.";
    }

    private long MinSizeBytes => AutoMinFileSizeBytes;

    [RelayCommand]
    private void AddFolder()
    {
        // handled by the view via a folder picker; this is a fallback for typed input.
    }

    public void AddRoot(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !ScanPaths.Contains(path))
        {
            ScanPaths.Add(path);
            PersistPaths();
        }
    }

    [RelayCommand]
    private void RemoveRoot(string? path)
    {
        if (path is not null && ScanPaths.Contains(path))
        {
            ScanPaths.Remove(path);
            PersistPaths();
        }
    }

    private void PersistPaths()
    {
        AppServices.Settings.Settings.CleanupScanPaths = ScanPaths.ToList();
        AppServices.Settings.Save();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Erzeugt einen lesbaren Text-Bericht über die aktuell gefundenen
    /// Duplikate (inkl. Markierung, Größe und Löschmodus).</summary>
    public string BuildReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Gaming Hub – Disk-Cleanup-Bericht");
        sb.AppendLine($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"Löschmodus: {(UseRecycleBin ? "Papierkorb (wiederherstellbar)" : "Endgültig löschen (nicht wiederherstellbar)")}");
        sb.AppendLine($"Suchordner: {(ScanPaths.Count == 0 ? "–" : string.Join("; ", ScanPaths))}");
        sb.AppendLine($"Gruppen: {GroupsCount} · Freigebbar: {WastedDisplay}");
        sb.AppendLine();

        int index = 1;
        foreach (var g in Groups)
        {
            sb.AppendLine($"[{index++}] {g.Header}");
            sb.AppendLine($"    Freigebbar: {g.WastedDisplay}");
            foreach (var f in g.Files)
            {
                string mark = f.IsSelected ? "[LÖSCHEN]" : "[BEHALTEN]";
                sb.AppendLine($"    {mark} {f.SizeDisplay,10}  {f.Path}");
            }
            sb.AppendLine();
        }

        if (Groups.Count == 0)
            sb.AppendLine("Keine doppelten Dateien gefunden.");

        return sb.ToString();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
            return;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        Groups.Clear();
        UpdateStats();
        StatusText = "Prüfung läuft…";
        _stopwatch.Restart();
        ScanPercent = 0;
        ElapsedDisplay = "0sek";
        EtaDisplay = "Verbleibend: wird berechnet…";
        _lastFraction = -1;

        var options = new DuplicateScanOptions
        {
            Roots = ScanPaths.ToList(),
            MinFileSizeBytes = MinSizeBytes
        };

        var progress = new Progress<DuplicateScanProgress>(p =>
        {
            // Gesamtfortschritt über alle Phasen (0..1), damit die Restzeit
            // auch bei sehr großen Mengen fortlaufend geschätzt werden kann.
            double frac = -1;
            if (p.TotalFiles > 0 && p.FilesInspected > 0)
            {
                double within = Math.Clamp((double)p.FilesInspected / p.TotalFiles, 0, 1);
                if (p.Phase.Contains("indiziert", StringComparison.OrdinalIgnoreCase))
                    frac = 0.10 + 0.45 * within;
                else if (p.Phase.Contains("verglichen", StringComparison.OrdinalIgnoreCase))
                    frac = 0.55 + 0.45 * within;
            }

            if (p.Percent >= 0)
                ScanPercent = Math.Clamp(p.Percent, 0, 100);

            if (p.TotalFiles > 0)
                ProgressPhase = $"{p.Phase} – {p.FilesInspected:N0} / {p.TotalFiles:N0} Dateien";
            else
                ProgressPhase = $"{p.Phase} – {p.FilesInspected:N0} Dateien geprüft…";

            UpdateEta(frac);
        });

        try
        {
            var groups = await AppServices.DuplicateFinder.FindDuplicatesAsync(options, progress, _cts.Token);
            foreach (var g in groups)
                Groups.Add(new DuplicateGroupViewModel(g));
            UpdateStats();
            AppServices.Badges.Grant("aufraeumer");
            _stopwatch.Stop();
            ElapsedDisplay = Format.Elapsed(_stopwatch.Elapsed);
            EtaDisplay = string.Empty;
            StatusText = groups.Count == 0
                ? $"Keine doppelten Dateien gefunden (Dauer: {ElapsedDisplay})."
                : $"{groups.Count} Gruppen doppelter Dateien gefunden (Dauer: {ElapsedDisplay}).";
        }
        catch (OperationCanceledException)
        {
            _stopwatch.Stop();
            EtaDisplay = string.Empty;
            StatusText = "Prüfung abgebrochen.";
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            EtaDisplay = string.Empty;
            StatusText = "Fehler: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
            ProgressPhase = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void SelectAllGroups()
    {
        foreach (var g in Groups)
            g.SelectAllButOne();
    }

    [RelayCommand]
    private void DeselectAllGroups()
    {
        foreach (var g in Groups)
            g.SelectNone();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var toDelete = new List<string>();
        foreach (var g in Groups)
        {
            var selected = g.SelectedFiles.ToList();
            var remaining = g.Files.Where(f => !f.IsSelected).Count();
            // Ensure at least one copy survives per group.
            if (remaining == 0 && selected.Count > 0)
                selected.RemoveAt(0);
            toDelete.AddRange(selected.Select(f => f.Path));
        }

        if (toDelete.Count == 0)
        {
            StatusText = "Keine Dateien zum Löschen ausgewählt.";
            return;
        }

        StatusText = $"Lösche {toDelete.Count} Datei(en)…";
        var result = await Task.Run(() =>
            AppServices.FileDeletion.Delete(toDelete, UseRecycleBin));

        StatusText = $"{result.Deleted} Datei(en) gelöscht, {Format.Bytes(result.BytesFreed)} freigegeben." +
                     (result.Failed > 0 ? $" {result.Failed} fehlgeschlagen." : string.Empty);
        RemoveResolvedGroups();
        UpdateStats();
    }

    [RelayCommand]
    private async Task FullAutoCleanAsync()
    {
        if (Groups.Count == 0)
        {
            StatusText = "Noch keine Duplikate gefunden. Starte zuerst die Prüfung.";
            return;
        }

        foreach (var g in Groups)
            g.SelectAllButOne();

        await DeleteSelectedAsync();
        StatusText = "Full Auto Clean abgeschlossen. " + StatusText;
    }

    private void RemoveResolvedGroups()
    {
        for (int i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].Files.Count(f => File.Exists(f.Path)) <= 1)
                Groups.RemoveAt(i);
        }
    }

    private void UpdateStats()
    {
        GroupsCount = Groups.Count;
        long wasted = Groups.Sum(g => g.Files.Count > 1 ? g.FileSize * (g.Files.Count - 1) : 0);
        WastedDisplay = Format.Bytes(wasted);
    }

    /// <summary>Live-Restzeitschätzung aus dem Gesamtfortschritt (0..1).
    /// Nutzt die Steigung zwischen zwei Messpunkten – funktioniert auch, wenn
    /// sich der gerundete Prozentwert über lange Zeit nicht ändert.
    /// Zeigt immer einen Wert an, sobald ein Fortschrittsbruch vorliegt.</summary>
    private void UpdateEta(double fraction)
    {
        if (fraction < 0)
        {
            if (IsScanning)
                EtaDisplay = "Verbleibend: wird berechnet…";
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastFraction >= 0 && fraction > _lastFraction + 0.0005)
        {
            double seconds = (now - _lastFractionAtUtc).TotalSeconds;
            double rate = (fraction - _lastFraction) / Math.Max(seconds, 0.2);
            double remainingSec = rate > 0 ? (1.0 - fraction) / rate : double.PositiveInfinity;
            EtaDisplay = remainingSec is > 1 and < 6 * 3600
                ? "Verbleibend: ca. " + Format.Elapsed(TimeSpan.FromSeconds(remainingSec))
                : "Verbleibend: wird berechnet…";
        }
        else if (EtaDisplay.StartsWith("Verbleibend: wird"))
        {
            // Noch keine valide Rate – trotzdem einen groben Schätzwert zeigen,
            // falls wir schon ein paar Prozent haben.
            if (fraction > 0.02)
                EtaDisplay = "Verbleibend: ca. " + Format.Elapsed(TimeSpan.FromSeconds(fraction / 0.02 * 10));
        }

        _lastFraction = fraction;
        _lastFractionAtUtc = now;
    }
}
