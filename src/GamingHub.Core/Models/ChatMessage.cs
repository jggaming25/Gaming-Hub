using System.Text.Json.Serialization;

namespace GamingHub.Core.Models;

/// <summary>A single chat message (channel "allgemein" or a direct-message channel).</summary>
public sealed class ChatMessage
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("channel_id")] public string ChannelId { get; set; } = "allgemein";
    [JsonPropertyName("author_id")] public string? AuthorId { get; set; }
    [JsonPropertyName("author_name")] public string? AuthorName { get; set; }
    [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A chat partner shown in the contact list.</summary>
public sealed class ChatContact
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    [JsonPropertyName("last_seen")] public DateTimeOffset? LastSeen { get; set; }

    public string Name => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName!
        : (!string.IsNullOrWhiteSpace(Email) ? Email! : "Unbekannt");

    public bool IsOnline => LastSeen is { } seen && (DateTimeOffset.UtcNow - seen) < TimeSpan.FromSeconds(90);
}
