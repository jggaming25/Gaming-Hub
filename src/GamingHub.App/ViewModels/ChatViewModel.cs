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
    public string? FriendshipId { get; init; }
    public bool IsFriend { get; init; }

    public bool IsDirect => !IsGeneral;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private int _unread;
    [ObservableProperty] private bool _isBlocked;

    public bool HasUnread => Unread > 0;
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    partial void OnUnreadChanged(int value) => OnPropertyChanged(nameof(HasUnread));
}

/// <summary>A friend request or accepted friend shown in the friends list.</summary>
public sealed partial class FriendItem : ObservableObject
{
    public string FriendshipId { get; init; } = string.Empty;
    public string OtherId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsAccepted { get; init; }
    public bool Incoming { get; init; }

    public bool IsIncomingRequest => !IsAccepted && Incoming;
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}

/// <summary>A rendered chat message.</summary>
public sealed class ChatMessageItem
{
    public string Id { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public bool IsOwn { get; init; }
    public bool Deleted { get; init; }
    public string? ReplyPreview { get; init; }
    public string? AttachmentUrl { get; init; }
    public string? AttachmentName { get; init; }
    public string? AttachmentSizeText { get; init; }
    public string Initial => string.IsNullOrEmpty(AuthorName) ? "?" : AuthorName[..1].ToUpperInvariant();

    public bool IsDeleted => Deleted;
    public bool HasReply => !string.IsNullOrWhiteSpace(ReplyPreview);
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentUrl);
    public bool IsImageAttachment => AttachmentName is not null &&
        (AttachmentName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
         AttachmentName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
         AttachmentName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
         AttachmentName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
         AttachmentName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
}

public partial class ChatViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTimeOffset> _lastTimestamps = new();
    private HashSet<string> _blocked = new();
    private bool _polling;
    private bool _started;
    private bool _switching;
    private int _pollCount;

    public ObservableCollection<ChatTargetItem> Targets { get; } = new();
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<FriendItem> Friends { get; } = new();

    [ObservableProperty] private ChatTargetItem? _selectedTarget;
    [ObservableProperty] private string _messageInput = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isBusy;

    // Direktnachricht per E-Mail
    [ObservableProperty] private string _dmEmailInput = string.Empty;
    [ObservableProperty] private string _dmEmailStatus = string.Empty;

    // Freundschaften
    [ObservableProperty] private string _friendEmailInput = string.Empty;
    [ObservableProperty] private string _friendStatus = string.Empty;

    // Antwort (Reply)
    [ObservableProperty] private string? _replyToId;
    [ObservableProperty] private string? _replyPreview;
    public bool IsReplying => !string.IsNullOrWhiteSpace(ReplyToId);
    partial void OnReplyToIdChanged(string? value) => OnPropertyChanged(nameof(IsReplying));
    partial void OnReplyPreviewChanged(string? value) => OnPropertyChanged(nameof(IsReplying));

    // Anhänge / Feature-Flags
    [ObservableProperty] private string _attachmentName = string.Empty;
    [ObservableProperty] private bool _isUploading;

    [ObservableProperty] private bool _chatEnabled = true;
    [ObservableProperty] private bool _dmEnabled = true;
    [ObservableProperty] private bool _voiceEnabled = true;
    [ObservableProperty] private bool _videoEnabled = true;
    [ObservableProperty] private bool _filesEnabled = true;

    private bool _isAdmin;

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
    public bool CanCallSelected => IsLoggedIn && DmEnabled && SelectedTarget?.IsDirect == true && !AppServices.Calls.IsBusy;
    public bool CanCallVoice => CanCallSelected && VoiceEnabled;
    public bool CanCallVideo => CanCallSelected && VideoEnabled;
    public bool CanUseFiles => IsLoggedIn && FilesEnabled;
    public bool CanBlockSelected => SelectedTarget?.IsDirect == true && !SelectedTarget.IsBlocked;
    public bool CanUnblockSelected => SelectedTarget?.IsDirect == true && SelectedTarget.IsBlocked;

    public bool DesktopNotificationsEnabled
    {
        get => AppServices.Settings.Settings.DesktopNotificationsEnabled;
        set { AppServices.Settings.Settings.DesktopNotificationsEnabled = value; AppServices.Settings.Save(); OnPropertyChanged(); }
    }

    public bool ShowLoginHint => !IsLoggedIn;
    public bool HasNoMessages => Messages.Count == 0;
    public bool HasFriends => Friends.Count > 0;
    public bool HasNoFriends => Friends.Count == 0;

    public ChatViewModel()
    {
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMessages));
        Friends.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFriends));
            OnPropertyChanged(nameof(HasNoFriends));
        };

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
        OnPropertyChanged(nameof(CanCallVoice));
        OnPropertyChanged(nameof(CanCallVideo));
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

    private bool IsAdmin()
        => AppServices.Auth.CurrentUser is { } u &&
           AppServices.Profile?.IsAdmin == true;

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

        await LoadFlagsAsync();
        _blocked = await AppServices.Chat.LoadBlockedIdsAsync();
        await RefreshFriendsAsync();
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
        Friends.Clear();
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

    private async Task LoadFlagsAsync()
    {
        var flags = await AppServices.Chat.LoadFlagsAsync();
        _isAdmin = AppServices.Profile?.IsAdmin == true;
        bool Flag(string key) => !flags.TryGetValue(key, out var v) || v || _isAdmin;

        ChatEnabled = Flag("chat_enabled");
        DmEnabled = Flag("dm_enabled");
        VoiceEnabled = Flag("voice_enabled");
        VideoEnabled = Flag("video_enabled");
        FilesEnabled = Flag("files_enabled");
        RaiseCallProps();
        OnPropertyChanged(nameof(CanUseFiles));
    }

    partial void OnSelectedTargetChanged(ChatTargetItem? value)
    {
        OnPropertyChanged(nameof(CanCallSelected));
        OnPropertyChanged(nameof(CanCallVoice));
        OnPropertyChanged(nameof(CanCallVideo));
        OnPropertyChanged(nameof(CanBlockSelected));
        OnPropertyChanged(nameof(CanUnblockSelected));
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
            CancelReply();
            Messages.Clear();

            var history = await AppServices.Chat.LoadAsync(target.ChannelId);
            foreach (var m in history)
                if (!_blocked.Contains(m.AuthorId ?? string.Empty))
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
    private void Reply(ChatMessageItem? item)
    {
        if (item is null || item.IsDeleted) return;
        ReplyToId = item.Id;
        ReplyPreview = $"{item.AuthorName}: {item.Body}";
    }

    [RelayCommand]
    private void CancelReply()
    {
        ReplyToId = null;
        ReplyPreview = null;
    }

    [RelayCommand]
    private void OpenAttachment(ChatMessageItem? item)
    {
        if (item?.AttachmentUrl is { Length: > 0 } url)
            AppServices.OpenBrowser(url);
    }

    [RelayCommand]
    private async Task DeleteMessage(ChatMessageItem? item)
    {
        if (item is null || !item.IsOwn) return;
        await AppServices.Chat.DeleteMessageAsync(item.Id);
        await PollAsync();
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = MessageInput?.Trim();
        if (string.IsNullOrEmpty(text) || SelectedTarget is null) return;
        if (!IsLoggedIn) { StatusText = "Bitte zuerst anmelden."; return; }
        if (!ChatEnabled) { StatusText = "Der Chat wurde vom Administrator deaktiviert."; return; }
        if (SelectedTarget.IsDirect && !DmEnabled) { StatusText = "Direktnachrichten sind deaktiviert."; return; }

        var target = SelectedTarget;
        var replyId = ReplyToId;
        var replyPrev = ReplyPreview;
        MessageInput = string.Empty;
        CancelReply();

        var outcome = await AppServices.Chat.SendAsync(target.ChannelId, text, replyId, replyPrev);
        if (!outcome.Ok)
        {
            StatusText = "Nachricht konnte nicht gesendet werden: " + (outcome.Error ?? "unbekannter Fehler");
            return;
        }
        StatusText = string.Empty;
        AppendSent(outcome.Message, target.ChannelId);
        await PollAsync();
    }

    /// <summary>Shows the sent message immediately (local echo) and remembers the new timestamp.</summary>
    private void AppendSent(ChatMessage? msg, string channelId)
    {
        if (msg is null || msg.Id is not { Length: > 0 })
            return;

        if (Messages.All(m => m.Id != msg.Id))
            AppendMessage(msg);
        _lastTimestamps[channelId] = msg.CreatedAt;
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        if (SelectedTarget is null || !CanUseFiles || IsUploading) return;
        var picker = PickFileRequested;
        if (picker is null) return;
        string? path = await picker();
        if (!string.IsNullOrEmpty(path))
            await SendFilePathAsync(path);
    }

    /// <summary>The view sets this to open a native file picker.</summary>
    public Func<Task<string?>>? PickFileRequested { get; set; }

    /// <summary>Called from the view after a file picker returns a path.</summary>
    public async Task SendFilePathAsync(string path)
    {
        if (SelectedTarget is null || !IsLoggedIn || IsUploading) return;
        if (!CanUseFiles) { StatusText = "Dateiversand ist deaktiviert."; return; }

        IsUploading = true;
        AttachmentName = Path.GetFileName(path);
        StatusText = $"Lade „{AttachmentName}\" hoch…";
        try
        {
            byte[] data = await Task.Run(() => File.ReadAllBytes(path));
            var (url, error) = await AppServices.Chat.UploadAttachmentAsync(SelectedTarget.ChannelId, Path.GetFileName(path), data);
            if (url is null)
            {
                StatusText = "Upload fehlgeschlagen: " + (error ?? "unbekannter Fehler");
                return;
            }

            var replyId = ReplyToId;
            var replyPrev = ReplyPreview;
            CancelReply();
            var outcome = await AppServices.Chat.SendAsync(SelectedTarget.ChannelId, MessageInput?.Trim() ?? string.Empty,
                replyId, replyPrev, url, Path.GetFileName(path), data.Length);
            StatusText = outcome.Ok ? string.Empty : "Nachricht konnte nicht gesendet werden: " + outcome.Error;
            MessageInput = string.Empty;
            AppendSent(outcome.Message, SelectedTarget.ChannelId);
            await PollAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Upload fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsUploading = false;
            AttachmentName = string.Empty;
        }
    }

    // ---------------- DM per E-Mail ----------------

    [RelayCommand]
    private async Task AddDmByEmailAsync()
    {
        string email = DmEmailInput?.Trim() ?? string.Empty;
        if (!IsValidEmail(email))
        {
            DmEmailStatus = "Bitte eine gültige E-Mail-Adresse eingeben.";
            return;
        }
        if (!DmEnabled)
        {
            DmEmailStatus = "Direktnachrichten sind deaktiviert.";
            return;
        }

        DmEmailStatus = "Suche Nutzer…";
        var user = await AppServices.Chat.FindUserByEmailAsync(email);
        if (user is null)
        {
            DmEmailStatus = "Kein Nutzer mit dieser E-Mail gefunden.";
            return;
        }
        if (user.Id == AppServices.Chat.MyId)
        {
            DmEmailStatus = "Das ist deine eigene E-Mail.";
            return;
        }

        _blocked.Remove(user.Id);
        string me = AppServices.Chat.MyId ?? string.Empty;
        var existing = Targets.FirstOrDefault(t => t.ContactId == user.Id);
        if (existing is not null)
        {
            SwitchTarget(existing);
        }
        else
        {
            var target = new ChatTargetItem
            {
                ChannelId = ChatService.DirectChannelId(me, user.Id),
                Name = user.Name,
                ContactId = user.Id,
                IsOnline = user.IsOnline
            };
            Targets.Add(target);
            SwitchTarget(target);
        }
        DmEmailStatus = $"Chat mit {user.Name} geöffnet.";
        DmEmailInput = string.Empty;
    }

    private void SwitchTarget(ChatTargetItem target) => _ = SwitchTargetAsync(target);

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        int at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@')) return false;
        string domain = email[(at + 1)..];
        return domain.Contains('.') && domain.IndexOf('.') is > 0 and < int.MaxValue - 1
               && !email.Contains(' ');
    }

    // ---------------- Sperren ----------------

    [RelayCommand]
    private async Task BlockSelectedAsync()
    {
        var t = SelectedTarget;
        if (t is not { IsDirect: true }) return;
        if (await AppServices.Chat.BlockAsync(t.ContactId))
        {
            _blocked.Add(t.ContactId);
            t.IsBlocked = true;
            OnPropertyChanged(nameof(CanBlockSelected));
            OnPropertyChanged(nameof(CanUnblockSelected));
            StatusText = $"{t.Name} wurde blockiert.";
            await SwitchTargetAsync(t);
        }
    }

    [RelayCommand]
    private async Task UnblockSelectedAsync()
    {
        var t = SelectedTarget;
        if (t is not { IsDirect: true }) return;
        if (await AppServices.Chat.UnblockAsync(t.ContactId))
        {
            _blocked.Remove(t.ContactId);
            t.IsBlocked = false;
            OnPropertyChanged(nameof(CanBlockSelected));
            OnPropertyChanged(nameof(CanUnblockSelected));
            StatusText = $"{t.Name} wurde entsperrt.";
            await SwitchTargetAsync(t);
        }
    }

    // ---------------- Freundschaften ----------------

    [RelayCommand]
    private async Task AddFriendByEmailAsync()
    {
        string email = FriendEmailInput?.Trim() ?? string.Empty;
        if (!IsValidEmail(email))
        {
            FriendStatus = "Bitte eine gültige E-Mail-Adresse eingeben.";
            return;
        }
        FriendStatus = "Sende Anfrage…";
        var (ok, error) = await AppServices.Chat.SendFriendRequestAsync(email);
        FriendStatus = ok ? "Freundschaftsanfrage gesendet." : "Fehler: " + error;
        if (ok)
        {
            FriendEmailInput = string.Empty;
            await RefreshFriendsAsync();
        }
    }

    [RelayCommand]
    private async Task AcceptFriendAsync(FriendItem? item)
    {
        if (item is null) return;
        if (await AppServices.Chat.AcceptFriendAsync(item.FriendshipId))
        {
            FriendStatus = $"{item.Name} ist jetzt dein Freund.";
            await RefreshFriendsAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveFriendAsync(FriendItem? item)
    {
        if (item is null) return;
        if (await AppServices.Chat.RemoveFriendAsync(item.FriendshipId))
        {
            FriendStatus = $"Freundschaft mit {item.Name} entfernt.";
            await RefreshFriendsAsync();
        }
    }

    [RelayCommand]
    private async Task CallVoiceAsync()
    {
        if (SelectedTarget is { IsDirect: true } t && CanCallVoice)
            await AppServices.Calls.StartCallAsync(t.ContactId, t.Name, video: false);
    }

    [RelayCommand]
    private async Task CallVideoAsync()
    {
        if (SelectedTarget is { IsDirect: true } t && CanCallVideo)
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
                    if (incoming.Count > 0)
                        _lastTimestamps[target.ChannelId] = incoming[^1].CreatedAt;
                    continue;
                }

                if (incoming.Count == 0) continue;
                _lastTimestamps[target.ChannelId] = incoming[^1].CreatedAt;

                foreach (var msg in incoming)
                {
                    if (msg.AuthorId == AppServices.Chat.MyId) continue;
                    if (_blocked.Contains(msg.AuthorId ?? string.Empty)) continue;

                    if (target.ChannelId == SelectedTarget?.ChannelId)
                    {
                        if (Messages.All(m => m.Id != msg.Id))
                            AppendMessage(msg);
                    }
                    else
                    {
                        target.Unread++;
                        AppServices.Notifications.NewMessage(DisplayName(target), msg.AuthorName ?? "Jemand", msg.Body);
                    }
                }
            }

            if (++_pollCount % 5 == 0)
                await RefreshFriendsAsync();
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

    private async Task RefreshFriendsAsync()
    {
        var friends = await AppServices.Chat.LoadFriendsAsync();
        string me = AppServices.Chat.MyId ?? string.Empty;

        Friends.Clear();
        foreach (var f in friends)
            Friends.Add(new FriendItem
            {
                FriendshipId = f.FriendshipId,
                OtherId = f.OtherId,
                Name = f.OtherName,
                IsAccepted = f.IsAccepted,
                Incoming = f.Incoming
            });

        // Akzeptierte Freunde als DM-Ziele anbieten (ohne vollständige Profilliste).
        var accepted = friends.Where(f => f.IsAccepted).ToList();
        var byContactId = Targets.Where(t => !t.IsGeneral).ToDictionary(t => t.ContactId);
        var acceptedIds = accepted.Select(a => a.OtherId).ToHashSet();

        foreach (var f in accepted)
        {
            if (f.OtherId == me) continue;
            if (byContactId.TryGetValue(f.OtherId, out var existing))
                existing.IsOnline = existing.IsOnline;
            else
                Targets.Add(new ChatTargetItem
                {
                    ChannelId = ChatService.DirectChannelId(me, f.OtherId),
                    Name = f.OtherName,
                    ContactId = f.OtherId,
                    FriendshipId = f.FriendshipId,
                    IsFriend = true
                });
        }

        // Nicht (mehr) befreundete, automatisch angelegte Ziele entfernen,
        // manuell per E-Mail geöffnete Chats bleiben erhalten.
        foreach (var t in Targets.Where(t => !t.IsGeneral && t.IsFriend && !acceptedIds.Contains(t.ContactId)).ToList())
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
        Id = m.Id,
        AuthorName = string.IsNullOrWhiteSpace(m.AuthorName) ? "Unbekannt" : m.AuthorName!,
        Body = m.Deleted ? "Nachricht gelöscht" : m.Body,
        Deleted = m.Deleted,
        TimeText = m.CreatedAt.ToLocalTime().ToString("HH:mm"),
        IsOwn = m.AuthorId == AppServices.Chat.MyId,
        ReplyPreview = m.ReplyPreview,
        AttachmentUrl = m.Deleted ? null : m.AttachmentUrl,
        AttachmentName = m.AttachmentName,
        AttachmentSizeText = m.AttachmentSize > 0 ? Format.Bytes(m.AttachmentSize) : null
    };
}
