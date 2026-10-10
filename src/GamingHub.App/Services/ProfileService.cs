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
            Changed?.Invoke();
            return;
        }

        try
        {
            var res = await client.SelectAsync("profiles",
                $"select=is_admin,is_banned&id=eq.{Uri.EscapeDataString(id)}");
            if (res.Ok)
            {
                var list = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(res.Body);
                if (list is { Count: > 0 })
                {
                    IsAdmin = list[0].TryGetValue("is_admin", out var a) && a.ValueKind == JsonValueKind.True;
                    IsBanned = list[0].TryGetValue("is_banned", out var b) && b.ValueKind == JsonValueKind.True;
                }
            }
        }
        catch { /* ignore */ }

        Changed?.Invoke();
    }
}
