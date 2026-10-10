using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public partial class CleanupViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> ScanPaths { get; } = new();
    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();
    public ObservableCollection<DriveScanOption> Drives { get; } = new();

    public long[] MinSizeChoices { get; } = { 1, 10, 100, 1024 };

    [ObservableProperty] private int _minSizeIndex = 1;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Wähle Ordner und starte die Prüfung auf doppelte Dateien.";
    [ObservableProperty] private string _progressPhase = string.Empty;
    [ObservableProperty] private int _groupsCount;
    [ObservableProperty] private string _wastedDisplay = "0 B";

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

    private long MinSizeBytes => MinSizeChoices[Math.Clamp(MinSizeIndex, 0, MinSizeChoices.Length - 1)] * 1024L * 1024L;

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

        var options = new DuplicateScanOptions
        {
            Roots = ScanPaths.ToList(),
            MinFileSizeBytes = MinSizeBytes
        };

        var progress = new Progress<DuplicateScanProgress>(p =>
        {
            ProgressPhase = $"{p.Phase} – {p.FilesInspected} Dateien geprüft";
        });

        try
        {
            var groups = await AppServices.DuplicateFinder.FindDuplicatesAsync(options, progress, _cts.Token);
            foreach (var g in groups)
                Groups.Add(new DuplicateGroupViewModel(g));
            UpdateStats();
            AppServices.Badges.Grant("aufraeumer");
            StatusText = groups.Count == 0
                ? "Keine doppelten Dateien gefunden."
                : $"{groups.Count} Gruppen doppelter Dateien gefunden.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Prüfung abgebrochen.";
        }
        catch (Exception ex)
        {
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
}
