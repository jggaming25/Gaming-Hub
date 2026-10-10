using System.Text.Json;
using SIPSorcery.Net;

namespace GamingHub.App.Services;

public enum CallState { Idle, RingingIn, CallingOut, Connecting, Active }

/// <summary>
/// Voice/video calls built on WebRTC (SIPSorcery) with Supabase as the
/// signalling channel. Offers/answers/ICE candidates are exchanged through the
/// call_signals table, polled every 1-2 seconds.
/// </summary>
public sealed class CallService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private WebRtcPeer? _peer;
    private string? _callId;
    private bool _isCaller;
    private DateTimeOffset _lastSignalAt = DateTimeOffset.UtcNow.AddSeconds(-5);
    private DateTime _outgoingStartedUtc;
    private DateTime _ringingStartedUtc;
    private CancellationTokenSource? _cts;
    private bool _running;

    public CallState State { get; private set; } = CallState.Idle;
    public string? PeerId { get; private set; }
    public string? PeerName { get; private set; }
    public bool VideoCall { get; private set; }
    public string? StatusText { get; private set; }
    public bool IsBusy => State != CallState.Idle;

    // UI-Events (werden vom Poll-Thread ausgelöst -> ViewModel marshallt auf UI-Thread).
    public event Action? Changed;
    public event Action<byte[], int, int>? LocalVideoFrame;
    public event Action<byte[], int, int>? RemoteVideoFrame;
    public event Action<string>? IncomingCall;   // Name des Anrufers
    public event Action<string>? MissedCall;     // Name des Anrufers
    public event Action<string?>? Ended;         // optionale Meldung

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastSignalAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _running = false;
        try { _cts?.Cancel(); } catch { }
        EndCall(null);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (AppServices.Auth.IsLoggedIn)
            {
                try { await PollSignalsAsync(); } catch { }
                CheckTimeouts();
            }
            try { await Task.Delay(State == CallState.Idle ? 2000 : 1000, ct); }
            catch { break; }
        }
    }

    // ---------------- Öffentliche Steuerung ----------------

    public async Task StartCallAsync(string peerId, string peerName, bool video)
    {
        if (IsBusy) return;
        try
        {
            _callId = Guid.NewGuid().ToString();
            _isCaller = true;
            PeerId = peerId;
            PeerName = peerName;
            VideoCall = video;
            State = CallState.CallingOut;
            StatusText = "Klingelt…";
            _outgoingStartedUtc = DateTime.UtcNow;
            Changed?.Invoke();

            await AppServices.Chat.SendSignalAsync(_callId, peerId, "invite", new { video, name = AppServices.Chat.MyName });
        }
        catch { EndCall("Anruf konnte nicht gestartet werden"); }
    }

    public async Task AcceptAsync()
    {
        if (State != CallState.RingingIn || _callId is null || PeerId is null) return;
        try
        {
            State = CallState.Connecting;
            StatusText = "Verbinde…";
            Changed?.Invoke();
            await AppServices.Chat.SendSignalAsync(_callId, PeerId, "accept", null);
            EnsurePeer();
        }
        catch { EndCall(null); }
    }

    public async Task DeclineAsync()
    {
        if (State != CallState.RingingIn || _callId is null || PeerId is null) return;
        try { await AppServices.Chat.SendSignalAsync(_callId, PeerId, "decline", null); } catch { }
        EndCall(null);
    }

    public async Task HangupAsync()
    {
        if (_callId is not null && PeerId is not null)
        {
            try { await AppServices.Chat.SendSignalAsync(_callId, PeerId, "hangup", null); } catch { }
        }
        EndCall(null);
    }

    public void ToggleVideo()
    {
        // Senden des Videostroms ein-/ausschalten (Kamera bleibt initialisiert).
        VideoCall = !VideoCall;
        Changed?.Invoke();
    }

    // ---------------- Signaling ----------------

    private async Task PollSignalsAsync()
    {
        var signals = await AppServices.Chat.LoadSignalsAsync(_lastSignalAt);
        foreach (var s in signals)
        {
            if (s.CreatedAt > _lastSignalAt) _lastSignalAt = s.CreatedAt;
            try { await HandleSignalAsync(s.Kind, s.SenderId, s.CallId, s.Payload ?? string.Empty); } catch { }
            await AppServices.Chat.DeleteSignalAsync(s.Id);
        }
    }

    private async Task HandleSignalAsync(string kind, string senderId, string signalCallId, string payload)
    {
        switch (kind)
        {
            case "invite":
                if (IsBusy) { await AppServices.Chat.SendSignalAsync(signalCallId, senderId, "decline", null); return; }
                _callId = signalCallId;
                var invite = ParseInvite(payload);
                VideoCall = invite.video;
                PeerId = senderId;
                PeerName = invite.name;
                State = CallState.RingingIn;
                StatusText = "Eingehender Anruf";
                _ringingStartedUtc = DateTime.UtcNow;
                Changed?.Invoke();
                IncomingCall?.Invoke(PeerName ?? "Unbekannt");
                break;

            case "accept":
                if (!_isCaller || senderId != PeerId || State != CallState.CallingOut) return;
                State = CallState.Connecting;
                StatusText = "Verbinde…";
                Changed?.Invoke();
                EnsurePeer();
                await SendOfferAsync();
                break;

            case "decline":
                if (senderId != PeerId) return;
                EndCall("Anruf abgelehnt");
                break;

            case "offer":
                if (senderId != PeerId || State is CallState.Idle or CallState.CallingOut) return;
                EnsurePeer();
                var offer = ParseSdp(payload);
                if (offer is null) return;
                var answer = await _peer!.CreateAnswerAsync(offer);
                if (_callId is not null && PeerId is not null)
                    await AppServices.Chat.SendSignalAsync(_callId, PeerId, "answer", new { sdp = answer.sdp });
                break;

            case "answer":
                if (senderId != PeerId || !_isCaller) return;
                var answerSdp = ParseSdp(payload);
                if (answerSdp is not null) _peer?.SetRemoteAnswer(answerSdp);
                break;

            case "ice":
                if (senderId != PeerId || string.IsNullOrWhiteSpace(payload)) return;
                _peer?.AddRemoteIce(payload);
                break;

            case "hangup":
                if (senderId != PeerId) return;
                if (State == CallState.RingingIn) { MissedCall?.Invoke(PeerName ?? "Unbekannt"); EndCall(null); }
                else EndCall("Anruf beendet");
                break;
        }
    }

    private async Task SendOfferAsync()
    {
        if (_peer is null || _callId is null || PeerId is null) return;
        var offer = await _peer.CreateOfferAsync();
        await AppServices.Chat.SendSignalAsync(_callId, PeerId, "offer", new { sdp = offer.sdp });
    }

    private void EnsurePeer()
    {
        if (_peer is not null) return;
        _peer = new WebRtcPeer(VideoCall);
        _peer.LocalIce += async json =>
        {
            try
            {
                if (_callId is not null && PeerId is not null)
                    await AppServices.Chat.SendRawSignalAsync(_callId, PeerId, "ice", json);
            }
            catch { }
        };
        _peer.LocalVideoFrame += (b, w, h) => LocalVideoFrame?.Invoke(b, w, h);
        _peer.RemoteVideoFrame += (b, w, h) => RemoteVideoFrame?.Invoke(b, w, h);
        _peer.StateChanged += state =>
        {
            if (state == RTCPeerConnectionState.connected)
            {
                _peer?.StartMedia();
                if (State is CallState.Connecting or CallState.CallingOut)
                {
                    State = CallState.Active;
                    StatusText = "Verbunden";
                    Changed?.Invoke();
                }
            }
            else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed or RTCPeerConnectionState.disconnected)
            {
                if (State == CallState.Active) EndCall("Verbindung verloren");
            }
        };
    }

    private void CheckTimeouts()
    {
        if (State == CallState.CallingOut && (DateTime.UtcNow - _outgoingStartedUtc).TotalSeconds > 40)
            EndCall("Keine Antwort");
        else if (State == CallState.RingingIn && (DateTime.UtcNow - _ringingStartedUtc).TotalSeconds > 30)
        {
            MissedCall?.Invoke(PeerName ?? "Unbekannt");
            _ = DeclineAsync();
        }
    }

    private void EndCall(string? message)
    {
        var peer = _peer;
        _peer = null;
        try { peer?.Dispose(); } catch { }

        bool wasBusy = State != CallState.Idle;
        State = CallState.Idle;
        PeerId = null;
        PeerName = null;
        VideoCall = false;
        _callId = null;
        _isCaller = false;
        StatusText = null;

        if (wasBusy) Changed?.Invoke();
        if (message is not null) Ended?.Invoke(message);
    }

    private static (bool video, string? name) ParseInvite(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            bool video = root.TryGetProperty("video", out var v) && v.ValueKind == JsonValueKind.True;
            string? name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            return (video, name);
        }
        catch { return (false, null); }
    }

    private static string? ParseSdp(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("sdp", out var s) ? s.GetString() : null;
        }
        catch { return null; }
    }
}
