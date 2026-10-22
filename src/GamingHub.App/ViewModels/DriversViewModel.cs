using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public sealed partial class DriverRow : ObservableObject
{
    public InstalledDriverInfo Info { get; }
    public string DeviceName => Info.DeviceName;
    public string DeviceClass => string.IsNullOrWhiteSpace(Info.DeviceClass) ? "–" : Info.DeviceClass;
    public string Provider => string.IsNullOrWhiteSpace(Info.Provider) ? "–" : Info.Provider;
    public string Version => Info.Version;
    public string DriverDate => string.IsNullOrWhiteSpace(Info.DriverDate) ? "–" : Info.DriverDate;

    public DriverRow(InstalledDriverInfo info) => Info = info;
}

public sealed partial class DriverUpdateRow : ObservableObject
{
    public DriverUpdateInfo Info { get; }
    public string Title => Info.Title;
    public string Provider => string.IsNullOrWhiteSpace(Info.DriverProvider) ? "–" : Info.DriverProvider;
    public string Version => string.IsNullOrWhiteSpace(Info.DriverVersion) ? "–" : Info.DriverVersion;
    public string DriverClass => string.IsNullOrWhiteSpace(Info.DriverClass) ? "–" : Info.DriverClass;
    public string DriverDate => string.IsNullOrWhiteSpace(Info.DriverDate) ? "–" : Info.DriverDate;
    public string SizeText => Info.SizeBytes > 0 ? Format.Bytes(Info.SizeBytes) : "–";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _statusColor = "#8B91AC";
    [ObservableProperty] private bool _isInstalling;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public DriverUpdateRow(DriverUpdateInfo info) => Info = info;
}

public partial class DriversViewModel : ViewModelBase
{
    private List<DriverRow> _allDrivers = new();

    public ObservableCollection<DriverRow> Drivers { get; } = new();
    public ObservableCollection<DriverUpdateRow> Updates { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCheckingUpdates;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _statusText = "Treiber werden geladen…";
    [ObservableProperty] private string _updateStatusText = "Noch nicht nach Updates gesucht.";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _driverCountText = "–";
    [ObservableProperty] private bool _hasUpdates;
    [ObservableProperty] private bool _isAdministrator;

    public bool IsNotAdministrator => !IsAdministrator;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnIsAdministratorChanged(bool value) => OnPropertyChanged(nameof(IsNotAdministrator));

    public DriversViewModel()
    {
        IsAdministrator = DriverUpdateService.IsAdministrator;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Lese installierte Treiber über WMI…";
        try
        {
            var rows = await Task.Run(() =>
                DriverUpdateService.GetInstalledDrivers()
                    .Select(d => new DriverRow(d))
                    .ToList());

            _allDrivers = rows;
            ApplyFilter();
            StatusText = $"{rows.Count} Treiber gefunden.";
        }
        catch (Exception ex)
        {
            StatusText = "Treiber konnten nicht gelesen werden: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        Drivers.Clear();
        foreach (var row in _allDrivers)
        {
            if (query.Length == 0 ||
                row.DeviceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Provider.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.DeviceClass.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Version.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Drivers.Add(row);
            }
        }
        DriverCountText = query.Length == 0
            ? $"{_allDrivers.Count} Treiber"
            : $"{Drivers.Count} von {_allDrivers.Count} Treibern";
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        UpdateStatusText = "Frage Windows Update nach Treiber-Updates ab…";
        try
        {
            var (updates, error) = await Task.Run(() =>
            {
                var list = DriverUpdateService.SearchDriverUpdates(out var err);
                return (list, err);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Updates.Clear();
                foreach (var update in updates)
                    Updates.Add(new DriverUpdateRow(update));
                HasUpdates = Updates.Count > 0;

                UpdateStatusText = error is not null
                    ? "Windows Update konnte nicht abgefragt werden: " + error
                    : Updates.Count == 0
                        ? "Keine Treiber-Updates verfügbar – alles aktuell."
                        : $"{Updates.Count} Treiber-Update(s) verfügbar.";

                AppServices.Notifications.ShowCompleted(
                    "Treiberprüfung abgeschlossen",
                    error is not null
                        ? "Windows Update konnte nicht abgefragt werden."
                        : Updates.Count == 0
                            ? "Keine Treiber-Updates verfügbar – alles aktuell."
                            : $"{Updates.Count} Treiber-Update(s) verfügbar.");
            });
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync(DriverUpdateRow? row)
    {
        if (row is null || row.IsInstalling || IsInstalling) return;

        IsInstalling = true;
        row.IsInstalling = true;
        row.StatusText = "Installation wird vorbereitet…";
        row.StatusColor = "#FACC15";

        try
        {
            DriverInstallResult? result;
            if (DriverUpdateService.IsAdministrator)
            {
                row.StatusText = "Lädt herunter und installiert…";
                result = await Task.Run(() => DriverUpdateService.InstallUpdate(row.Info.UpdateId));
            }
            else
            {
                row.StatusText = "Administratorrechte werden angefordert…";
                result = await RunElevatedInstallAsync(row.Info.UpdateId, row);
            }

            if (result is null)
            {
                row.StatusText = "Installation wurde abgebrochen oder lieferte kein Ergebnis.";
                row.StatusColor = "#F87171";
            }
            else
            {
                row.StatusText = result.Message;
                row.StatusColor = result.Success ? "#4ADE80" : "#F87171";
                if (result.Success)
                    UpdateStatusText = result.RebootRequired
                        ? "Treiber installiert – bitte Windows neu starten."
                        : "Treiber erfolgreich installiert.";

                AppServices.Notifications.ShowCompleted(
                    result.Success ? "Treiberinstallation abgeschlossen" : "Treiberinstallation fehlgeschlagen",
                    result.Message);
            }

            await RefreshAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            row.StatusText = "UAC-Dialog abgebrochen – es wurden keine Änderungen vorgenommen.";
            row.StatusColor = "#FB923C";
        }
        catch (Exception ex)
        {
            row.StatusText = "Fehler: " + ex.Message;
            row.StatusColor = "#F87171";
        }
        finally
        {
            row.IsInstalling = false;
            IsInstalling = false;
        }
    }

    private static async Task<DriverInstallResult?> RunElevatedInstallAsync(string updateId, DriverUpdateRow row)
    {
        string exe = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exe))
            return new DriverInstallResult { Message = "Programmpfad konnte nicht ermittelt werden." };

        string resultPath = Path.Combine(Path.GetTempPath(), "GamingHub",
            $"driver-install-{Guid.NewGuid():N}.json");
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"--install-driver \"{updateId}\" --result \"{resultPath}\""
        };

        var process = Process.Start(startInfo);
        if (process is null)
            return new DriverInstallResult { Message = "Der Installationsprozess konnte nicht gestartet werden." };

        row.StatusText = "Warte auf Abschluss der Installation (UAC)…";
        await process.WaitForExitAsync();

        var result = DriverUpdateService.ReadResult(resultPath);
        try { File.Delete(resultPath); } catch { }
        return result ?? new DriverInstallResult
        {
            Message = "Installation beendet, aber keine Rückmeldung erhalten."
        };
    }

    [RelayCommand]
    private void OpenWindowsUpdate()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:windowsupdate")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }
}
