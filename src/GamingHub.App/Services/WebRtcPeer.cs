using System.Net;
using System.Text.Json;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using Vpx.Net;

namespace GamingHub.App.Services;

/// <summary>
/// Thin wrapper around a SIPSorcery WebRTC peer connection that uses the Windows
/// microphone/speaker/webcam and the pure-C# VP8 codec. Audio and video are
/// initialised defensively: if a device or the media stack is unavailable the
/// call still works without that medium.
/// </summary>
internal sealed class WebRtcPeer : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly RTCPeerConnection _pc;
    private WindowsAudioEndPoint? _audio;
    private WindowsVideoEndPoint? _video;
    private readonly bool _videoEnabled;
    private readonly List<RTCIceCandidateInit> _pendingRemoteIce = new();
    private bool _remoteDescriptionSet;
    private bool _disposed;

    /// <summary>Local ICE candidate as a JSON string (RTCIceCandidateInit).</summary>
    public event Action<string>? LocalIce;
    public event Action<byte[], int, int>? LocalVideoFrame;   // BGRA8888
    public event Action<byte[], int, int>? RemoteVideoFrame;  // BGRA8888
    public event Action<RTCPeerConnectionState>? StateChanged;

    public WebRtcPeer(bool videoEnabled)
    {
        _videoEnabled = videoEnabled;

        var config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>
            {
                new RTCIceServer { urls = "stun:stun.cloudflare.com" },
                new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
            }
        };
        _pc = new RTCPeerConnection(config);

        TryInitAudio();
        if (videoEnabled) TryInitVideo();

        _pc.onicecandidate += c => { if (c is not null) LocalIce?.Invoke(c.toJSON()); };
        _pc.onconnectionstatechange += st => StateChanged?.Invoke(st);
    }

    private void TryInitAudio()
    {
        try
        {
            _audio = new WindowsAudioEndPoint(new AudioEncoder());
            var track = new MediaStreamTrack(_audio.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
            _pc.addTrack(track);
            _audio.OnAudioSourceEncodedSample += _pc.SendAudio;
            _pc.OnAudioFormatsNegotiated += formats =>
            {
                if (formats.Count > 0)
                {
                    try { _audio.SetAudioSourceFormat(formats[0]); } catch { }
                    try { _audio.SetAudioSinkFormat(formats[0]); } catch { }
                }
            };
            _pc.OnRtpPacketReceived += (ep, media, rtp) =>
            {
                if (media != SDPMediaTypesEnum.audio) return;
                try
                {
                    _audio.GotAudioRtp(ep, rtp.Header.SyncSource, rtp.Header.SequenceNumber,
                        rtp.Header.Timestamp, rtp.Header.PayloadType, rtp.Header.MarkerBit != 0, rtp.Payload);
                }
                catch { }
            };
        }
        catch { _audio = null; }
    }

    private void TryInitVideo()
    {
        try
        {
            _video = new WindowsVideoEndPoint(new VP8Codec());
            var track = new MediaStreamTrack(_video.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
            _pc.addTrack(track);
            _video.OnVideoSourceEncodedSample += _pc.SendVideo;
            _pc.OnVideoFormatsNegotiated += formats =>
            {
                if (formats.Count > 0)
                {
                    try { _video.SetVideoSourceFormat(formats[0]); } catch { }
                }
            };
            _pc.OnVideoFrameReceived += _video.GotVideoFrame;
            _video.OnVideoSourceRawSample += (_, w, h, sample, _) =>
                LocalVideoFrame?.Invoke(BgrToBgra(sample, w, h), w, h);
            _video.OnVideoSinkDecodedSample += (sample, w, h, _, _) =>
                RemoteVideoFrame?.Invoke(BgrToBgra(sample, (int)w, (int)h), (int)w, (int)h);
        }
        catch { _video = null; }
    }

    public async Task<RTCSessionDescriptionInit> CreateOfferAsync()
    {
        var offer = _pc.createOffer(null);
        await _pc.setLocalDescription(offer);
        return offer;
    }

    public async Task<RTCSessionDescriptionInit> CreateAnswerAsync(string offerSdp)
    {
        _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offerSdp });
        _remoteDescriptionSet = true;
        FlushPendingIce();
        var answer = _pc.createAnswer(null);
        await _pc.setLocalDescription(answer);
        return answer;
    }

    public void SetRemoteAnswer(string answerSdp)
    {
        _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answerSdp });
        _remoteDescriptionSet = true;
        FlushPendingIce();
    }

    public void AddRemoteIce(string candidateJson)
    {
        try
        {
            var init = JsonSerializer.Deserialize<RTCIceCandidateInit>(candidateJson, Json);
            if (init is null) return;
            if (!_remoteDescriptionSet) { _pendingRemoteIce.Add(init); return; }
            _pc.addIceCandidate(init);
        }
        catch { }
    }

    private void FlushPendingIce()
    {
        foreach (var ice in _pendingRemoteIce)
        {
            try { _pc.addIceCandidate(ice); } catch { }
        }
        _pendingRemoteIce.Clear();
    }

    public void StartMedia()
    {
        try { _ = _audio?.Start(); } catch { }
        try { _ = _video?.StartVideo(); } catch { }
    }

    public bool HasVideo => _videoEnabled && _video is not null;
    public bool HasAudio => _audio is not null;

    private static byte[] BgrToBgra(byte[] bgr, int width, int height)
    {
        int pixels = width * height;
        var bgra = new byte[pixels * 4];
        int src = 0, dst = 0;
        for (int i = 0; i < pixels; i++)
        {
            bgra[dst++] = bgr[src++];
            bgra[dst++] = bgr[src++];
            bgra[dst++] = bgr[src++];
            bgra[dst++] = 255;
        }
        return bgra;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _pc.close(); } catch { }
        try { _ = _audio?.Close(); } catch { }
        try { _ = _video?.Close(); } catch { }
    }
}
