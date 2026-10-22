using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

/// <summary>Hilfe und Problemlösung mit direkten Sprüngen zu den
/// passenden Windows-Einstellungen sowie Diagnosefunktionen.</summary>
public partial class HelpViewModel : ViewModelBase
{
    public string AppVersion => AppInfo.VersionDisplay;
    public string DataDirectory => AppServices.Settings.DataDirectory;
    public string LogFile => LogService.LogFile;

    public string ContactEmail => "genzmannjann@gmail.com";

    [ObservableProperty] private string _copyStatusText = string.Empty;

    [RelayCommand]
    private void OpenNotificationSettings()
    {
        AppServices.Notifications.RefreshNativeStatus();
        Open(AppServices.Notifications.NotificationSettingsUri);
    }

    [RelayCommand]
    private void OpenWindowsUpdate() => Open("ms-settings:windowsupdate");

    [RelayCommand]
    private void OpenNetworkSettings() => Open("ms-settings:network-status");

    [RelayCommand]
    private void OpenDeviceManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(LogService.LogFolder);

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(AppServices.Settings.DataDirectory);

    [RelayCommand]
    private void OpenContactEmail() =>
        AppServices.OpenEmail(ContactEmail, "Gaming Hub Hilfe");

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Gaming Hub – Diagnose");
            sb.AppendLine($"Version: {AppInfo.VersionDisplay}");
            sb.AppendLine($"Windows: {RuntimeInformation.OSDescription.Trim()}");
            sb.AppendLine($"Benachrichtigungen: {AppServices.Notifications.NativeStatus}");
            sb.AppendLine($"Datenordner: {AppServices.Settings.DataDirectory}");
            sb.AppendLine($"Logdatei: {LogService.LogFile}");
            sb.AppendLine($"Zeitpunkt: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as
                IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetValueAsync(DataFormat.Text, sb.ToString());
                CopyStatusText = "Diagnose in die Zwischenablage kopiert.";
            }
            else
            {
                CopyStatusText = "Keine Zwischenablage verfügbar.";
            }
        }
        catch (Exception ex)
        {
            CopyStatusText = "Kopieren fehlgeschlagen: " + ex.Message;
        }
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
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
