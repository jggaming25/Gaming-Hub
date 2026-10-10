using System.Text.Json.Serialization;

namespace GamingHub.Core.Models;

/// <summary>A WebRTC signalling message exchanged through Supabase (call_signals table).</summary>
public sealed class CallSignal
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("call_id")] public string CallId { get; set; } = string.Empty;
    [JsonPropertyName("sender_id")] public string SenderId { get; set; } = string.Empty;
    [JsonPropertyName("recipient_id")] public string RecipientId { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty; // invite|accept|decline|offer|answer|ice|hangup
    [JsonPropertyName("payload")] public string? Payload { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
