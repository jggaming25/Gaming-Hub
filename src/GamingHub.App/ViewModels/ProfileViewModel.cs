using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GamingHub.App.ViewModels;

public partial class ProfileViewModel : ViewModelBase
{
    [ObservableProperty] private string _statusText = "Cloud-Login wird in Phase 5 mit Supabase aktiviert.";
    [ObservableProperty] private string _displayName = "Gast (nicht angemeldet)";

    public string VersionDisplay => AppInfo.VersionDisplay;

    [RelayCommand]
    private void NotAvailableYet()
        => StatusText = "Login per E-Mail, Google oder Discord folgt mit der Supabase-Anbindung.";
}
