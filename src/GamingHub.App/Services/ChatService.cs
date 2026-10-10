using System.Text.Json;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.Services;

/// <summary>
/// Text chat and presence backed by Supabase (PostgREST). Phase 6 uses polling
/// (every few seconds) instead of a websocket, which keeps the client
/// dependency-free. Calls build on the same channel + the call_signals table.
/// </summary>
public sealed class ChatService
{
    public const string GeneralChannel = "allgemein";
    public const string GeneralChannelName = "Allgemein";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private SupabaseClient? Client => AppServices.Auth.Client;
    public bool Available => Client is not null && AppServices.Auth.IsLoggedIn;
    public string? MyId => AppServices.Auth.CurrentUser?.Id;
    public string MyName => AppServices.Auth.CurrentUser?.DisplayName
                            ?? AppServices.Auth.CurrentUser?.Email
                            ?? "Ich";

    /// <summary>Static channel id for a direct conversation between two users.</summary>
    public static string DirectChannelId(string a, string b)
    {
        var (x, y) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        return $"dm-{x}-{y}";
    }

    /// <summary>Loads messages. When <paramref name="sinceUtc"/> is null the newest ones are returned.</summary>
    public async Task<List<ChatMessage>> LoadAsync(string channelId, DateTimeOffset? sinceUtc = null, int limit = 60)
    {
        var client = Client;
        if (client is null) return new();

        string ch = Uri.EscapeDataString(channelId);
        string query = sinceUtc is { } since
            ? $"select=*&channel_id=eq.{ch}&created_at=gt.{Uri.EscapeDataString(since.UtcDateTime.ToString("o"))}&order=created_at.asc&limit={limit}"
            : $"select=*&channel_id=eq.{ch}&order=created_at.desc&limit={limit}";

        var result = await client.SelectAsync("chat_messages", query);
        if (!result.Ok) return new();

        var list = JsonSerializer.Deserialize<List<ChatMessage>>(result.Body, Json) ?? new();
        if (sinceUtc is null) list.Reverse();
        return list;
    }

    public async Task<bool> SendAsync(string channelId, string body)
    {
        var client = Client;
        if (client is null || string.IsNullOrWhiteSpace(body)) return false;

        var row = new Dictionary<string, object?>
        {
            ["channel_id"] = channelId,
            ["author_id"] = MyId,
            ["author_name"] = MyName,
            ["body"] = body.Trim()
        };
        var result = await client.InsertAsync("chat_messages", row);
        return result.Ok;
    }

    public async Task<List<ChatContact>> LoadContactsAsync()
    {
        var client = Client;
        if (client is null) return new();

        var result = await client.SelectAsync("profiles",
            "select=id,display_name,email,avatar_url,last_seen&order=display_name.asc.nullslast");
        if (!result.Ok) return new();

        var all = JsonSerializer.Deserialize<List<ChatContact>>(result.Body, Json) ?? new();
        return all.Where(c => c.Id != MyId).ToList();
    }

    public async Task HeartbeatAsync()
    {
        var client = Client;
        if (client is null || MyId is not { Length: > 0 } id) return;
        await client.PatchAsync("profiles", $"id=eq.{Uri.EscapeDataString(id)}",
            new { last_seen = DateTime.UtcNow });
    }

    // ---------------- Anruf-Signaling ----------------

    public async Task<bool> SendSignalAsync(string callId, string recipientId, string kind, object? payload = null)
    {
        return await InsertSignalAsync(callId, recipientId, kind,
            payload is null ? null : JsonSerializer.Serialize(payload));
    }

    /// <summary>Sends a signal whose payload is already a JSON string (e.g. an ICE candidate).</summary>
    public async Task<bool> SendRawSignalAsync(string callId, string recipientId, string kind, string rawPayload)
    {
        return await InsertSignalAsync(callId, recipientId, kind, rawPayload);
    }

    private async Task<bool> InsertSignalAsync(string callId, string recipientId, string kind, string? payload)
    {
        var client = Client;
        if (client is null || MyId is not { Length: > 0 } me) return false;

        var row = new Dictionary<string, object?>
        {
            ["call_id"] = callId,
            ["sender_id"] = me,
            ["recipient_id"] = recipientId,
            ["kind"] = kind,
            ["payload"] = payload
        };
        var result = await client.InsertAsync("call_signals", row);
        return result.Ok;
    }

    public async Task<List<CallSignal>> LoadSignalsAsync(DateTimeOffset? sinceUtc = null)
    {
        var client = Client;
        if (client is null || MyId is not { Length: > 0 } me) return new();

        string query = $"select=*&recipient_id=eq.{Uri.EscapeDataString(me)}" +
                       (sinceUtc is { } since
                           ? $"&created_at=gt.{Uri.EscapeDataString(since.UtcDateTime.ToString("o"))}"
                           : "") +
                       "&order=created_at.asc&limit=100";

        var result = await client.SelectAsync("call_signals", query);
        if (!result.Ok) return new();
        return JsonSerializer.Deserialize<List<CallSignal>>(result.Body, Json) ?? new();
    }

    public async Task DeleteSignalAsync(string signalId)
    {
        var client = Client;
        if (client is null) return;
        await client.DeleteAsync("call_signals", $"id=eq.{Uri.EscapeDataString(signalId)}");
    }
}
