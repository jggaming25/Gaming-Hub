using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;

namespace GamingHub.App.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    [ObservableProperty] private string _statusText =
        "Chat, Sprach- und Videochat folgen in Phase 6 (WebRTC + Supabase-Signaling).\nDesktop-Benachrichtigungen sind bereits aktiv.";

    public bool DesktopNotificationsEnabled
    {
        get => AppServices.Settings.Settings.DesktopNotificationsEnabled;
        set { AppServices.Settings.Settings.DesktopNotificationsEnabled = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    /// <summary>Called by the chat (Phase 6) whenever a message arrives.</summary>
    public static void NotifyNewMessage(string chatName, string sender, string text)
        => AppServices.Notifications.NewMessage(chatName, sender, text);

    /// <summary>Called by the chat (Phase 6) for a ringing incoming call.</summary>
    public static void NotifyIncomingCall(string caller)
        => AppServices.Notifications.IncomingCall(caller);

    /// <summary>Called by the chat (Phase 6) for a missed call.</summary>
    public static void NotifyMissedCall(string caller)
        => AppServices.Notifications.MissedCall(caller);

    [RelayCommand]
    private void DemoNewMessage() => NotifyNewMessage("Allgemein", "Maxi", "Hey, kommst du gleich zum Spielen?");

    [RelayCommand]
    private void DemoIncomingCall() => NotifyIncomingCall("Maxi");

    [RelayCommand]
    private void DemoMissedCall() => NotifyMissedCall("Maxi");
}