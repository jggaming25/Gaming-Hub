using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingHub.App.Services;
using GamingHub.Core.Models;

namespace GamingHub.App.ViewModels;

/// <summary>A selectable chat target: the "Allgemein" room or a direct conversation.</summary>
public sealed partial class ChatTargetItem : ObservableObject
{
    public string ChannelId { get; init; } = ChatService.GeneralChannel;
    public string Name { get; init; } = ChatService.GeneralChannelName;
    public bool IsGeneral { get; init; }
    public string ContactId { get; init; } = string.Empty;

    public bool IsDirect => !IsGeneral;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private int _unread;

    public bool HasUnread => Unread > 0;
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    partial void OnUnreadChanged(int value) => OnPropertyChanged(nameof(HasUnread));
}

/// <summary>A rendered chat message.</summary>
public sealed class ChatMessageItem
{
    public string AuthorName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public bool IsOwn { get; init; }
    public string Initial => string.IsNullOrEmpty(AuthorName) ? "?" : AuthorName[..1].ToUpperInvariant();
}

public partial class ChatViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTimeOffset> _lastTimestamps = new();
    private bool _polling;
    private bool _started;
    private bool _switching;
    private int _pollCount;

    public ObservableCollection<ChatTargetItem> Targets { get; } = new();
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    [ObservableProperty] private ChatTargetItem? _selectedTarget;
    [ObservableProperty] private string _messageInput = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isBusy;

    // ----- Anrufe (WebRTC) -----
    [ObservableProperty] private WriteableBitmap? _localVideo;
    [ObservableProperty] private WriteableBitmap? _remoteVideo;
    private bool _localFramePending;
    private bool _remoteFramePending;

    public bool CallActive => AppServices.Calls.IsBusy;
    public bool IsIncoming => AppServices.Calls.State == CallState.RingingIn;
    public bool IsCallConnected => AppServices.Calls.State == CallState.Active;
    public bool IsVideoCall => AppServices.Calls.VideoCall;
    public string CallPeerName => AppServices.Calls.PeerName ?? "…";
    public string CallStateText => AppServices.Calls.StatusText ?? string.Empty;
    public bool CanCallSelected => IsLoggedIn && SelectedTarget?.IsDirect == true && !AppServices.Calls.IsBusy;

    public bool DesktopNotificationsEnabled
    {
        get => AppServices.Settings.Settings.DesktopNotificationsEnabled;
        set { AppServices.Settings.Settings.DesktopNotificationsEnabled = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool ShowLoginHint => !IsLoggedIn;

    public bool HasNoMessages => Messages.Count == 0;

    public ChatViewModel()
    {
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMessages));

        Targets.Add(new ChatTargetItem
        {
            ChannelId = ChatService.GeneralChannel,
            Name = ChatService.GeneralChannelName,
            IsGeneral = true,
            IsOnline = true
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await PollAsync();

        AppServices.Auth.Changed += OnAuthChanged;
        WireCalls();
        _ = EvaluateAuthAsync();
    }

    private void WireCalls()
    {
        var calls = AppServices.Calls;
        calls.Changed += () => Dispatcher.UIThread.Post(() => RaiseCallProps());
        calls.IncomingCall += name => Dispatcher.UIThread.Post(() =>
        {
            AppServices.Notifications.IncomingCall(name);
            RaiseCallProps();
        });
        calls.MissedCall += name => Dispatcher.UIThread.Post(() => AppServices.Notifications.MissedCall(name));
        calls.Ended += msg => Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(msg)) StatusText = msg;
            RaiseCallProps();
        });
        calls.LocalVideoFrame += (b, w, h) => PostFrame(true, b, w, h);
        calls.RemoteVideoFrame += (b, w, h) => PostFrame(false, b, w, h);
    }

    private void RaiseCallProps()
    {
        OnPropertyChanged(nameof(CallActive));
        OnPropertyChanged(nameof(IsIncoming));
        OnPropertyChanged(nameof(IsCallConnected));
        OnPropertyChanged(nameof(IsVideoCall));
        OnPropertyChanged(nameof(CallPeerName));
        OnPropertyChanged(nameof(CallStateText));
        OnPropertyChanged(nameof(CanCallSelected));
    }

    private void PostFrame(bool local, byte[] bgra, int w, int h)
    {
        if (local) { if (_localFramePending) return; _localFramePending = true; }
        else { if (_remoteFramePending) return; _remoteFramePending = true; }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (local) { LocalVideo = RenderBitmap(LocalVideo, bgra, w, h); _localFramePending = false; }
                else { RemoteVideo = RenderBitmap(RemoteVideo, bgra, w, h); _remoteFramePending = false; }
            }
            catch { if (local) _localFramePending = false; else _remoteFramePending = false; }
        });
    }

    private static WriteableBitmap RenderBitmap(WriteableBitmap? existing, byte[] bgra, int w, int h)
    {
        if (w <= 0 || h <= 0) return existing!;
        var bmp = existing;
        if (bmp is null || bmp.PixelSize.Width != w || bmp.PixelSize.Height != h)
            bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using (var fb = bmp.Lock())
        {
            int bytes = Math.Min(bgra.Length, w * h * 4);
            Marshal.Copy(bgra, 0, fb.Address, bytes);
        }
        return bmp;
    }

    private async Task EvaluateAuthAsync()
    {
        IsLoggedIn = AppServices.Auth.IsLoggedIn;
        if (IsLoggedIn)
            await StartAsync();
        else
            UpdateStatus();
    }

    private void OnAuthChanged() => Dispatcher.UIThread.Post(async () =>
    {
        bool nowLoggedIn = AppServices.Auth.IsLoggedIn;
        if (nowLoggedIn && !_started)
            await StartAsync();
        else if (!nowLoggedIn && _started)
            Stop();
        else
        {
            IsLoggedIn = nowLoggedIn;
            UpdateStatus();
        }
    });

    private void UpdateStatus()
    {
        IsLoggedIn = AppServices.Auth.IsLoggedIn;
        OnPropertyChanged(nameof(ShowLoginHint));
        if (!AppServices.Auth.IsConfigured)
            StatusText = "Supabase ist noch nicht konfiguriert (siehe Einstellungen).";
        else if (!IsLoggedIn)
            StatusText = "Melde dich im Profil an, um zu chatten.";
        else
            StatusText = string.Empty;
    }

    private async Task StartAsync()
    {
        _started = true;
        IsLoggedIn = true;
        OnPropertyChanged(nameof(ShowLoginHint));
        StatusText = "Chat verbunden.";

        await RefreshContactsAsync();
        await SwitchTargetAsync(Targets.FirstOrDefault());
        _timer.Start();
        await PollAsync();
    }

    private void Stop()
    {
        _started = false;
        _timer.Stop();
        _lastTimestamps.Clear();
        Messages.Clear();
        Targets.Clear();
        Targets.Add(new ChatTargetItem
        {
            ChannelId = ChatService.GeneralChannel,
            Name = ChatService.GeneralChannelName,
            IsGeneral = true,
            IsOnline = true
        });
        SelectedTarget = null;
        UpdateStatus();
    }

    partial void OnSelectedTargetChanged(ChatTargetItem? value)
    {
        OnPropertyChanged(nameof(CanCallSelected));
        if (value is null) return;
        _ = SwitchTargetAsync(value);
    }

    private async Task SwitchTargetAsync(ChatTargetItem? target)
    {
        if (target is null || !IsLoggedIn) return;
        if (_switching) return;
        _switching = true;
        try
        {
            SelectedTarget = target;
            target.Unread = 0;
            Messages.Clear();

            var history = await AppServices.Chat.LoadAsync(target.ChannelId);
            foreach (var m in history)
                Messages.Add(ToItem(m));
            if (history.Count > 0)
                _lastTimestamps[target.ChannelId] = history[^1].CreatedAt;
            MessageScrolled?.Invoke();
        }
        finally
        {
            _switching = false;
        }
    }

    public event System.Action? MessageScrolled;

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = MessageInput?.Trim();
        if (string.IsNullOrEmpty(text) || SelectedTarget is null) return;
        if (!IsLoggedIn) { StatusText = "Bitte zuerst anmelden."; return; }

        MessageInput = string.Empty;
        bool ok = await AppServices.Chat.SendAsync(SelectedTarget.ChannelId, text);
        if (!ok)
        {
            StatusText = "Nachricht konnte nicht gesendet werden.";
            return;
        }
        StatusText = string.Empty;
        await PollAsync();
    }

    [RelayCommand]
    private async Task CallVoiceAsync()
    {
        if (SelectedTarget is { IsDirect: true } t)
            await AppServices.Calls.StartCallAsync(t.ContactId, t.Name, video: false);
    }

    [RelayCommand]
    private async Task CallVideoAsync()
    {
        if (SelectedTarget is { IsDirect: true } t)
            await AppServices.Calls.StartCallAsync(t.ContactId, t.Name, video: true);
    }

    [RelayCommand]
    private async Task AcceptCallAsync() => await AppServices.Calls.AcceptAsync();

    [RelayCommand]
    private async Task DeclineCallAsync() => await AppServices.Calls.DeclineAsync();

    [RelayCommand]
    private async Task HangupCallAsync() => await AppServices.Calls.HangupAsync();

    private async Task PollAsync()
    {
        if (_polling || !IsLoggedIn) return;
        _polling = true;
        try
        {
            await AppServices.Chat.HeartbeatAsync();

            foreach (var target in Targets.ToList())
            {
                DateTimeOffset? since = _lastTimestamps.TryGetValue(target.ChannelId, out var t) ? t : null;
                var incoming = await AppServices.Chat.LoadAsync(target.ChannelId, since);

                if (since is null)
                {
                    // Baseline festlegen, ohne die Historie als "neu" zu werten.
                    if (incoming.Count > 0)
                        _lastTimestamps[target.ChannelId] = incoming[^1].CreatedAt;
                    continue;
                }

                if (incoming.Count == 0) continue;
                _lastTimestamps[target.ChannelId] = incoming[^1].CreatedAt;

                foreach (var msg in incoming)
                {
                    if (msg.AuthorId == AppServices.Chat.MyId) continue;
                    if (target.ChannelId == SelectedTarget?.ChannelId)
                        AppendMessage(msg);
                    else
                    {
                        target.Unread++;
                        AppServices.Notifications.NewMessage(DisplayName(target), msg.AuthorName ?? "Jemand", msg.Body);
                    }
                }
            }

            if (++_pollCount % 5 == 0)
                await RefreshContactsAsync();
        }
        catch
        {
            // Netzwerkfehler ignorieren – der nächste Tick versucht es erneut.
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task RefreshContactsAsync()
    {
        var contacts = await AppServices.Chat.LoadContactsAsync();
        string me = AppServices.Chat.MyId ?? string.Empty;

        // Neue Kontakte ergänzen / Status aktualisieren, bestehende Objekte behalten.
        var byContactId = Targets.Where(t => !t.IsGeneral).ToDictionary(t => t.ContactId);
        var seen = new HashSet<string>();

        foreach (var c in contacts)
        {
            if (c.Id == me) continue;
            seen.Add(c.Id);
            if (byContactId.TryGetValue(c.Id, out var existing))
            {
                existing.IsOnline = c.IsOnline;
            }
            else
            {
                Targets.Add(new ChatTargetItem
                {
                    ChannelId = ChatService.DirectChannelId(me, c.Id),
                    Name = c.Name,
                    ContactId = c.Id,
                    IsOnline = c.IsOnline
                });
            }
        }

        // Entfernte Kontakte ausblenden.
        foreach (var t in Targets.Where(t => !t.IsGeneral && !seen.Contains(t.ContactId)).ToList())
            Targets.Remove(t);
    }

    private void AppendMessage(ChatMessage msg)
    {
        Messages.Add(ToItem(msg));
        MessageScrolled?.Invoke();
    }

    private static string DisplayName(ChatTargetItem target) => target.Name;

    private static ChatMessageItem ToItem(ChatMessage m) => new()
    {
        AuthorName = string.IsNullOrWhiteSpace(m.AuthorName) ? "Unbekannt" : m.AuthorName!,
        Body = m.Body,
        TimeText = m.CreatedAt.ToLocalTime().ToString("HH:mm"),
        IsOwn = m.AuthorId == AppServices.Chat.MyId
    };
}
