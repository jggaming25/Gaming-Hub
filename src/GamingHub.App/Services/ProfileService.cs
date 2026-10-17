using System.Text.Json;
using GamingHub.Core.Services;

namespace GamingHub.App.Services;

/// <summary>
/// Loads and caches the signed-in user's profile flags (admin / banned) so the
/// UI can gate admin-only features without extra round-trips.
/// </summary>
public sealed class ProfileService
{
    public bool IsAdmin { get; private set; }
    public bool IsBanned { get; private set; }

    /// <summary>True, wenn eine aktive Premium-Freischaltung vorliegt (längere
    /// Historie, mehr Diagramm-Verlauf).</summary>
    public bool IsPremium { get; private set; }

    /// <summary>Ablaufdatum der Premium-Freischaltung (null = kein/abgelaufenes Premium).</summary>
    public DateTimeOffset? PremiumUntil { get; private set; }

    /// <summary>Zeitpunkt, ab dem durchgehend Premium besteht (für das
    /// "3 Monate in Folge"-Abzeichen).</summary>
    public DateTimeOffset? PremiumSince { get; private set; }

    /// <summary>True, wenn ein Admin dem Nutzer Premium gesperrt hat.</summary>
    public bool PremiumBanned { get; private set; }

    /// <summary>Serverseitig vergebene Abzeichen-IDs des angemeldeten Nutzers.</summary>
    public HashSet<string> ServerBadges { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasServerBadge(string badgeId) => ServerBadges.Contains(badgeId);

    public event Action? Changed;

    public ProfileService()
    {
        AppServices.Auth.Changed += () => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var client = AppServices.Auth.Client;
        string? id = AppServices.Auth.CurrentUser?.Id;

        if (client is null || string.IsNullOrWhiteSpace(id))
        {
            IsAdmin = false;
            IsBanned = false;
            IsPremium = false;
            PremiumUntil = null;
            PremiumSince = null;
            PremiumBanned = false;
            ServerBadges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Changed?.Invoke();
            return;
        }

        try
        {
            var res = await client.SelectAsync("profiles",
                $"select=is_admin,is_banned,premium_until,premium_since,premium_banned&id=eq.{Uri.EscapeDataString(id)}");
            if (res.Ok)
            {
                var list = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(res.Body);
                if (list is { Count: > 0 })
                {
                    IsAdmin = list[0].TryGetValue("is_admin", out var a) && a.ValueKind == JsonValueKind.True;
                    IsBanned = list[0].TryGetValue("is_banned", out var b) && b.ValueKind == JsonValueKind.True;
                    PremiumBanned = list[0].TryGetValue("premium_banned", out var pb) && pb.ValueKind == JsonValueKind.True;

                    IsPremium = false;
                    PremiumUntil = null;
                    if (list[0].TryGetValue("premium_until", out var pu) && pu.ValueKind == JsonValueKind.String)
                    {
                        string? raw = pu.GetString();
                        if (raw is not null && raw.Equals("infinity", StringComparison.OrdinalIgnoreCase))
                        {
                            // Unendlich gültiges Premium (Code ohne Laufzeit/Gültig-bis).
                            PremiumUntil = DateTimeOffset.MaxValue;
                            IsPremium = true;
                        }
                        else if (raw is not null && DateTimeOffset.TryParse(raw, out var until))
                        {
                            PremiumUntil = until;
                            IsPremium = until > DateTimeOffset.UtcNow;
                        }
                    }

                    PremiumSince = null;
                    if (list[0].TryGetValue("premium_since", out var ps) && ps.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(ps.GetString(), out var since))
                        PremiumSince = since;
                }
            }

            // Serverseitig vergebene Abzeichen (z. B. 3 Monate Premium in Folge).
            var badgeRes = await client.SelectAsync("badges",
                $"select=badge_id&user_id=eq.{Uri.EscapeDataString(id)}");
            var badges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (badgeRes.Ok)
            {
                foreach (var row in JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(badgeRes.Body) ?? new())
                    if (row.TryGetValue("badge_id", out var bid) && bid.ValueKind == JsonValueKind.String)
                        badges.Add(bid.GetString()!);
            }
            ServerBadges = badges;
        }
        catch { /* ignore */ }

        Changed?.Invoke();
    }
}
