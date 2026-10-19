using System.Net.Http.Headers;
using System.Text.Json;
using GamingHub.Core.Services;

namespace GamingHub.App.Services;

/// <summary>Download-Zahlen einer einzelnen Version.</summary>
public sealed record VersionDownloadStat(string Version, DateTimeOffset? PublishedAt, long Downloads);

/// <summary>Aggregierte Admin-Kennzahlen.</summary>
public sealed class AdminStatistics
{
    public long TotalDownloads { get; init; }
    public long RegisteredUsers { get; init; }
    public bool UsersAvailable { get; init; }
    public DateTimeOffset? AppCreatedAt { get; init; }
    public IReadOnlyList<VersionDownloadStat> Versions { get; init; } = Array.Empty<VersionDownloadStat>();
    public string? DownloadsError { get; init; }
    public string? UsersError { get; init; }
}

/// <summary>
/// Lädt die Admin-Statistik: Downloads aller Versionen über die GitHub-Release-API,
/// registrierte Nutzer aus Supabase und das Erstellungsdatum der App.
/// </summary>
public sealed class AdminStatsService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GamingHub", AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<AdminStatistics> LoadAsync(CancellationToken ct = default)
    {
        var versions = new List<VersionDownloadStat>();
        long totalDownloads = 0;
        string? downloadsError = null;
        DateTimeOffset? earliestRelease = null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{RepoApiUrl}/releases?per_page=100");
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                downloadsError = $"GitHub-API antwortet mit {(int)resp.StatusCode}.";
            }
            else
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var release in doc.RootElement.EnumerateArray())
                    {
                        string version = release.TryGetProperty("tag_name", out var tag) &&
                                         tag.ValueKind == JsonValueKind.String
                            ? tag.GetString()!.TrimStart('v', 'V')
                            : "unbekannt";

                        DateTimeOffset? published = null;
                        if (release.TryGetProperty("published_at", out var pub) &&
                            pub.ValueKind == JsonValueKind.String &&
                            DateTimeOffset.TryParse(pub.GetString(), out var publishedAt))
                        {
                            published = publishedAt;
                            if (earliestRelease is null || publishedAt < earliestRelease)
                                earliestRelease = publishedAt;
                        }

                        long downloads = 0;
                        if (release.TryGetProperty("assets", out var assets) &&
                            assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                if (!asset.TryGetProperty("name", out var nameEl) ||
                                    nameEl.ValueKind != JsonValueKind.String)
                                    continue;

                                string name = nameEl.GetString() ?? string.Empty;
                                if (!IsDownloadAsset(name))
                                    continue;

                                if (asset.TryGetProperty("download_count", out var countEl) &&
                                    countEl.TryGetInt64(out long count))
                                    downloads += count;
                            }
                        }

                        versions.Add(new VersionDownloadStat(version, published, downloads));
                        totalDownloads += downloads;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            downloadsError = ex.Message;
        }

        versions.Sort((a, b) => (b.PublishedAt ?? DateTimeOffset.MinValue)
            .CompareTo(a.PublishedAt ?? DateTimeOffset.MinValue));

        long users = 0;
        bool usersAvailable = false;
        string? usersError = null;
        try
        {
            var client = AppServices.Auth.Client;
            if (client is null || !AppServices.Auth.IsLoggedIn)
            {
                usersError = "Nicht mit Supabase angemeldet.";
            }
            else
            {
                // Bevorzugt der serverseitige Admin-RPC (liefert nur die Anzahl,
                // keine personenbezogenen Daten). Fallback für ältere Schemata.
                var rpc = await client.RpcAsync("admin_user_count", new { });
                if (rpc.Ok && long.TryParse(rpc.Body.Trim(), out long rpcCount))
                {
                    users = rpcCount;
                    usersAvailable = true;
                }
                else
                {
                    var (ok, count) = await client.CountAsync("profiles", "select=id");
                    if (ok)
                    {
                        users = count;
                        usersAvailable = true;
                    }
                    else
                    {
                        usersError = "Nutzerzahl konnte nicht geladen werden.";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            usersError = ex.Message;
        }

        DateTimeOffset? appCreated = await GetRepositoryCreatedAsync(ct) ?? earliestRelease;

        return new AdminStatistics
        {
            TotalDownloads = totalDownloads,
            RegisteredUsers = users,
            UsersAvailable = usersAvailable,
            AppCreatedAt = appCreated,
            Versions = versions,
            DownloadsError = downloadsError,
            UsersError = usersError
        };
    }

    private static async Task<DateTimeOffset?> GetRepositoryCreatedAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RepoApiUrl);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("created_at", out var created) &&
                created.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(created.GetString(), out var value))
                return value;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Repo-Datum ist optional; Fallback ist das älteste Release.
        }

        return null;
    }

    private static string RepoApiUrl =>
        AppInfo.RepositoryUrl.Replace("https://github.com/", "https://api.github.com/repos/",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Zählt nur echte Download-Dateien (Setup, Portable-ZIP, vollständiges
    /// Paket) – keine Manifeste, Prüfsummen oder Deltas.</summary>
    private static bool IsDownloadAsset(string name)
    {
        if (name.EndsWith(".delta.nupkg", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
    }
}
