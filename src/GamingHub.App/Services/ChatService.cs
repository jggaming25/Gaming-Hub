using System.Text.Json;
using GamingHub.Core.Models;
using GamingHub.Core.Services;

namespace GamingHub.App.Services;

public sealed record SendOutcome(bool Ok, string? Error);

/// <summary>A friendship together with the other participant's display info.</summary>
public sealed record FriendEntry(string FriendshipId, string OtherId, string OtherName, string Status, bool Incoming)
{
    public bool IsAccepted => string.Equals(Status, "accepted", StringComparison.OrdinalIgnoreCase);
    public bool IsPending => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Text chat, direct messages, friendships, blocks and presence backed by
/// Supabase (PostgREST). Phase 6 uses polling instead of a websocket to stay
/// dependency-free. Calls build on the same channel + the call_signals table.
/// </summary>
public sealed class ChatService
{
    public const string GeneralChannel = "allgemein";
    public const string GeneralChannelName = "Allgemein";
    private const string AttachmentBucket = "chat-attachments";

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

    public async Task<SendOutcome> SendAsync(
        string channelId,
        string body,
        string? replyToId = null,
        string? replyPreview = null,
        string? attachmentUrl = null,
        string? attachmentName = null,
        long attachmentSize = 0)
    {
        var client = Client;
        if (client is null) return new SendOutcome(false, "Nicht angemeldet.");
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(attachmentUrl))
            return new SendOutcome(false, "Nachricht ist leer.");
        if (MyId is not { Length: > 0 })
            return new SendOutcome(false, "Kein gültiger Benutzer (bitte neu anmelden).");

        var row = new Dictionary<string, object?>
        {
            ["channel_id"] = channelId,
            ["author_id"] = MyId,
            ["author_name"] = MyName,
            ["body"] = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim(),
            ["reply_to_id"] = string.IsNullOrWhiteSpace(replyToId) ? null : replyToId,
            ["reply_preview"] = string.IsNullOrWhiteSpace(replyPreview) ? null : Truncate(replyPreview, 160),
            ["attachment_url"] = attachmentUrl,
            ["attachment_name"] = attachmentName,
            ["attachment_size"] = attachmentSize > 0 ? attachmentSize : null
        };

        var result = await client.InsertAsync("chat_messages", row);
        return result.Ok
            ? new SendOutcome(true, null)
            : new SendOutcome(false, string.IsNullOrWhiteSpace(result.Body) ? "Senden fehlgeschlagen." : result.Body);
    }

    /// <summary>Soft-deletes a message (author or admin; enforced by RLS).</summary>
    public async Task<bool> DeleteMessageAsync(string id)
    {
        var client = Client;
        if (client is null) return false;
        var res = await client.PatchAsync("chat_messages", $"id=eq.{Uri.EscapeDataString(id)}",
            new { deleted = true });
        return res.Ok;
    }

    // ---------------- Nutzer / E-Mail ----------------

    public async Task<ChatContact?> FindUserByEmailAsync(string email)
    {
        var client = Client;
        if (client is null || string.IsNullOrWhiteSpace(email)) return null;

        var res = await client.RpcAsync("find_user_by_email", new { p_email = email.Trim() });
        if (!res.Ok) return null;

        var list = JsonSerializer.Deserialize<List<ChatContact>>(res.Body, Json);
        return list is { Count: > 0 } ? list[0] : null;
    }

    public async Task<List<ChatContact>> LoadProfilesAsync(IEnumerable<string> ids)
    {
        var client = Client;
        var idList = ids.Distinct().Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        if (client is null || idList.Count == 0) return new();

        string inList = "(" + string.Join(',', idList.Select(Uri.EscapeDataString)) + ")";
        var res = await client.SelectAsync("profiles",
            $"select=id,display_name,email,avatar_url,last_seen&id=in.{inList}");
        if (!res.Ok) return new();
        return JsonSerializer.Deserialize<List<ChatContact>>(res.Body, Json) ?? new();
    }

    // ---------------- Dateianhang ----------------

    public async Task<(string? url, string? error)> UploadAttachmentAsync(string channelId, string fileName, byte[] data)
    {
        var client = Client;
        if (client is null) return (null, "Nicht angemeldet.");
        if (data.Length == 0) return (null, "Datei ist leer.");
        if (data.Length > 20 * 1024 * 1024) return (null, "Datei ist zu groß (max. 20 MB).");

        string safeName = string.Join('_', SanitizeFileName(fileName));
        string path = $"{SanitizeFileName(channelId)}/{Guid.NewGuid():N}-{safeName}";
        string contentType = GuessContentType(fileName);

        var res = await client.UploadAsync(AttachmentBucket, path, data, contentType);
        if (!res.Ok)
            return (null, res.Body);
        return (client.PublicStorageUrl(AttachmentBucket, path), null);
    }

    // ---------------- Sperre (Block) ----------------

    public async Task<HashSet<string>> LoadBlockedIdsAsync()
    {
        var client = Client;
        var set = new HashSet<string>();
        if (client is null || MyId is not { Length: > 0 } me) return set;

        var res = await client.SelectAsync("blocks", $"select=blocked_id&blocker_id=eq.{Uri.EscapeDataString(me)}");
        if (!res.Ok) return set;

        foreach (var row in JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(res.Body, Json) ?? new())
            if (row.TryGetValue("blocked_id", out var v) && v.ValueKind == JsonValueKind.String)
                set.Add(v.GetString()!);
        return set;
    }

    public async Task<bool> BlockAsync(string userId)
    {
        var client = Client;
        if (client is null || MyId is not { Length: > 0 } me) return false;
        var res = await client.InsertAsync("blocks", new { blocker_id = me, blocked_id = userId });
        return res.Ok;
    }

    public async Task<bool> UnblockAsync(string userId)
    {
        var client = Client;
        if (client is null || MyId is not { Length: > 0 } me) return false;
        var res = await client.DeleteAsync("blocks",
            $"blocker_id=eq.{Uri.EscapeDataString(me)}&blocked_id=eq.{Uri.EscapeDataString(userId)}");
        return res.Ok;
    }

    // ---------------- Freundschaften ----------------

    public async Task<List<FriendEntry>> LoadFriendsAsync()
    {
        var client = Client;
        var result = new List<FriendEntry>();
        if (client is null || MyId is not { Length: > 0 } me) return result;

        var res = await client.SelectAsync("friendships", "select=*&order=created_at.desc");
        if (!res.Ok) return result;

        var links = JsonSerializer.Deserialize<List<Friendship>>(res.Body, Json) ?? new();
        var otherIds = links.Select(l => l.RequesterId == me ? l.AddresseeId : l.RequesterId).Distinct().ToList();
        var profiles = (await LoadProfilesAsync(otherIds)).ToDictionary(p => p.Id, StringComparer.Ordinal);
        var myName = MyName;

        foreach (var link in links)
        {
            string otherId = link.RequesterId == me ? link.AddresseeId : link.RequesterId;
            profiles.TryGetValue(otherId, out var profile);
            result.Add(new FriendEntry(link.Id, otherId, profile?.Name ?? "Unbekannt", link.Status, link.AddresseeId == me));
        }
        _ = myName;
        return result;
    }

    public async Task<(bool ok, string? error)> SendFriendRequestAsync(string email)
    {
        var client = Client;
        if (client is null || MyId is not { Length: > 0 } me) return (false, "Nicht angemeldet.");

        var user = await FindUserByEmailAsync(email);
        if (user is null) return (false, "Kein Nutzer mit dieser E-Mail gefunden.");
        if (user.Id == me) return (false, "Das bist du selbst.");

        var res = await client.InsertAsync("friendships",
            new { requester_id = me, addressee_id = user.Id, status = "pending" });
        return res.Ok ? (true, null) : (false, res.Body);
    }

    public async Task<bool> AcceptFriendAsync(string friendshipId)
    {
        var client = Client;
        if (client is null) return false;
        var res = await client.PatchAsync("friendships", $"id=eq.{Uri.EscapeDataString(friendshipId)}",
            new { status = "accepted" });
        return res.Ok;
    }

    public async Task<bool> RemoveFriendAsync(string friendshipId)
    {
        var client = Client;
        if (client is null) return false;
        var res = await client.DeleteAsync("friendships", $"id=eq.{Uri.EscapeDataString(friendshipId)}");
        return res.Ok;
    }

    // ---------------- Feature-Flags (Admin-Schalter) ----------------

    public async Task<Dictionary<string, bool>> LoadFlagsAsync()
    {
        var client = Client;
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (client is null) return dict;

        var res = await client.SelectAsync("app_settings", "select=key,value");
        if (!res.Ok) return dict;

        foreach (var row in JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(res.Body, Json) ?? new())
        {
            if (row.TryGetValue("key", out var k) && k.ValueKind == JsonValueKind.String &&
                row.TryGetValue("value", out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                dict[k.GetString()!] = v.GetBoolean();
        }
        return dict;
    }

    public async Task<bool> SetFlagAsync(string key, bool value)
    {
        var client = Client;
        if (client is null) return false;
        var res = await client.PatchAsync("app_settings", $"key=eq.{Uri.EscapeDataString(key)}", new { value });
        return res.Ok;
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

    // ---------------- Helpers ----------------

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == '/' || c == '\\' || c == ' ' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "datei" : cleaned;
    }

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".zip" => "application/zip",
        ".mp4" => "video/mp4",
        ".mp3" => "audio/mpeg",
        _ => "application/octet-stream"
    };
}
