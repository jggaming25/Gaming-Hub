using System.Globalization;
using System.Text.Json;

namespace GamingHub.App.Services;

/// <summary>Nicht-monetäres Premium: Codes (aus der Admin-UI) oder über das
/// Erreichen von Abzeichen (Aktivität). Beides wird serverseitig per
/// Feature-Flag geschaltet und über RPC-Funktionen ohne Client-Cheat
/// eingelöst. Premium verlängert z. B. den Ressourcen-Verlauf auf 4 Wochen.</summary>
public sealed class PremiumService
{
    /// <summary>Abzeichen, deren Erreichen Aktivitäts-Premium gewährt.</summary>
    private static readonly HashSet<string> ActivityMilestones = new(StringComparer.OrdinalIgnoreCase)
    {
        "bibliothekar", "systemprofi", "aufraeumer", "bugreporter"
    };

    public bool IsPremium => AppServices.Profile.IsPremium;
    public DateTimeOffset? PremiumUntil => AppServices.Profile.PremiumUntil;

    /// <summary>True, wenn ein Admin Premium für dieses Konto gesperrt hat.</summary>
    public bool IsPremiumBanned => AppServices.Profile.PremiumBanned;

    public event Action? Changed;

    public PremiumService()
    {
        AppServices.Profile.Changed += () => Changed?.Invoke();
        AppServices.Badges.BadgeGranted += OnBadgeGranted;
    }

    public void Refresh() => Changed?.Invoke();

    /// <summary>Löst einen Freischalt-Code ein und verlängert Premium
    /// serverseitig (ein Code kann nur einmal verwendet werden).</summary>
    public async Task<(bool ok, string message, int bonusDays)> RedeemAsync(string code)
    {
        var client = AppServices.Auth.Client;
        if (client is null) return (false, "Du bist nicht angemeldet.", 0);
        if (string.IsNullOrWhiteSpace(code)) return (false, "Bitte einen Code eingeben.", 0);

        try
        {
            var before = AppServices.Profile.PremiumUntil;
            var res = await client.RpcAsync("redeem_premium_code", new { p_code = code.Trim() });
            if (res.Ok)
            {
                var parsed = TryParse(res.Body);
                if (parsed.TryGetValue("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    await AppServices.Profile.RefreshAsync();
                    var (days, infinite) = ExtractGrant(parsed, before);
                    Changed?.Invoke();
                    string message = infinite
                        ? "Premium dauerhaft aktiviert. Danke!"
                        : $"Premium um {days} {(days == 1 ? "Tag" : "Tage")} verlängert. Danke!";
                    return (true, message, days);
                }
                string key = parsed.TryGetValue("error", out var e) ? e.GetString() ?? "" : "";
                return (false, TranslateError(key), 0);
            }
            return (false, "Server nicht erreichbar.", 0);
        }
        catch (Exception ex)
        {
            return (false, "Fehler: " + ex.Message, 0);
        }
    }

    /// <summary>Meldet ein erreichtes Abzeichen als Aktivitäts-Meilenstein an
    /// (einmal pro Abzeichen, +7 Tage).</summary>
    public async Task<(bool ok, string message, int bonusDays)> ClaimActivityAsync(string milestone)
    {
        var client = AppServices.Auth.Client;
        if (client is null) return (false, "Nicht angemeldet.", 0);

        try
        {
            var res = await client.RpcAsync("claim_activity_premium", new { p_milestone = milestone });
            if (res.Ok)
            {
                var parsed = TryParse(res.Body);
                if (parsed.TryGetValue("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    await AppServices.Profile.RefreshAsync();
                    int days = parsed.TryGetValue("bonus_days", out var d) ? d.GetInt32() : 0;
                    Changed?.Invoke();
                    return (true, $"Für deine Aktivität: +{days} Tage Premium!", days);
                }
                return (false, TranslateError(parsed.TryGetValue("error", out var e) ? e.GetString() ?? "" : ""), 0);
            }
            return (false, "Server nicht erreichbar.", 0);
        }
        catch
        {
            return (false, "Fehler bei der Aktivitäts-Meldung.", 0);
        }
    }

    private async void OnBadgeGranted(string badgeId)
    {
        if (!ActivityMilestones.Contains(badgeId)) return;
        var (ok, _, days) = await ClaimActivityAsync(badgeId);
        if (ok && days > 0)
            AppServices.Notifications.Show("Premium aktiv",
                $"Für „{badgeLabel(badgeId)}“ gibt es {days} Tage Premium.");
    }

    private static string badgeLabel(string id) => id switch
    {
        "bibliothekar" => "Bibliothekar",
        "systemprofi" => "Systemprofi",
        "aufraeumer" => "Aufräumer",
        "bugreporter" => "Bugreporter",
        _ => id
    };

    /// <summary>Ermittelt, wie viele Tage ein Code gutgeschrieben hat.
    /// Bevorzugt den Server-Wert (<c>days_added</c>), sonst die Differenz des
    /// Premium-Endes vor/nach dem Einlösen, sonst <c>premium_until</c>.</summary>
    private static (int days, bool infinite) ExtractGrant(
        Dictionary<string, JsonElement> parsed, DateTimeOffset? before)
    {
        foreach (var key in new[] { "days_added", "bonus_days" })
        {
            if (parsed.TryGetValue(key, out var d) && d.ValueKind == JsonValueKind.Number)
                return (Math.Max(0, d.GetInt32()), false);
        }

        if (parsed.TryGetValue("premium_until", out var pu) && pu.ValueKind == JsonValueKind.String)
        {
            string? raw = pu.GetString();
            if (raw is not null && raw.Equals("infinity", StringComparison.OrdinalIgnoreCase))
                return (0, true);

            if (raw is not null &&
                DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until))
            {
                DateTimeOffset basis = before ?? DateTimeOffset.UtcNow;
                int days = (int)Math.Ceiling((until - basis).TotalDays);
                return (Math.Max(0, days), false);
            }
        }

        // Kein verwertbarer Server-Wert: aus dem aktualisierten Profil ableiten.
        var after = AppServices.Profile.PremiumUntil;
        if (after is null) return (0, false);
        DateTimeOffset start = before ?? DateTimeOffset.UtcNow;
        int delta = (int)Math.Ceiling((after.Value - start).TotalDays);
        return (Math.Max(0, delta), false);
    }

    private static Dictionary<string, JsonElement> TryParse(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static string TranslateError(string key) => key switch
    {
        "login_required" => "Bitte zuerst anmelden.",
        "codes_disabled" => "Codes sind zurzeit deaktiviert.",
        "activity_disabled" => "Aktivitäts-Premium ist zurzeit deaktiviert.",
        "already_claimed" => "Für dieses Abzeichen hast du bereits Premium erhalten.",
        "invalid_code" => "Diesen Code gibt es nicht.",
        "invalid_or_used" => "Code ungültig oder bereits verwendet.",
        "already_used" => "Diesen Code hast du bereits eingelöst.",
        "premium_banned" => "Premium wurde für dein Konto gesperrt.",
        _ => key
    };
}