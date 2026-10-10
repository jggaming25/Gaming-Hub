using CommunityToolkit.Mvvm.ComponentModel;

namespace GamingHub.App.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    [ObservableProperty] private string _statusText =
        "Chat, Sprach- und Videochat folgen in Phase 6 (WebRTC + Supabase-Signaling).";
}
