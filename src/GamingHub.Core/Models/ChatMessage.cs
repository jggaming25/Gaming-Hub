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

    [JsonPropertyName("reply_to_id")] public string? ReplyToId { get; set; }
    [JsonPropertyName("reply_preview")] public string? ReplyPreview { get; set; }

    [JsonPropertyName("attachment_url")] public string? AttachmentUrl { get; set; }
    [JsonPropertyName("attachment_name")] public string? AttachmentName { get; set; }
    [JsonPropertyName("attachment_size")] public long AttachmentSize { get; set; }

    [JsonPropertyName("deleted")] public bool Deleted { get; set; }

    [JsonIgnore] public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentUrl);
    [JsonIgnore] public bool HasReply => !string.IsNullOrWhiteSpace(ReplyPreview);
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

/// <summary>A friendship record between two users.</summary>
public sealed class Friendship
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("requester_id")] public string RequesterId { get; set; } = string.Empty;
    [JsonPropertyName("addressee_id")] public string AddresseeId { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }

    public bool IsAccepted => string.Equals(Status, "accepted", StringComparison.OrdinalIgnoreCase);
}
