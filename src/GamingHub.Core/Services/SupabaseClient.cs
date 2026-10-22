using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GamingHub.Core.Services;

public sealed class SupabaseUser
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
}

public sealed class SupabaseSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public SupabaseUser User { get; set; } = new();
}

public sealed record RestResult(bool Ok, string Body);

/// <summary>
/// Minimal, dependency-free client for Supabase Auth (password + PKCE OAuth) and
/// the PostgREST data API. Only the public anon key is required.
/// </summary>
public sealed class SupabaseClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new();

    public string Url { get; }
    public string AnonKey { get; }
    public SupabaseSession? Session { get; private set; }

    /// <summary>Wird ausgelöst, wenn sich die Sitzung ändert (Login, Refresh,
    /// Logout) – so kann die Sitzung dauerhaft gespeichert werden.</summary>
    public event Action? SessionChanged;

    public SupabaseClient(string url, string anonKey)
    {
        Url = url.TrimEnd('/');
        AnonKey = anonKey;
    }

    public bool IsLoggedIn => Session is not null && Session.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30);

    private void SetSession(SupabaseSession? session)
    {
        Session = session;
        SessionChanged?.Invoke();
    }

    public void RestoreSession(SupabaseSession? session) => SetSession(session);

    /// <summary>Stellt sicher, dass eine gültige Sitzung vorliegt. Läuft der
    /// Access-Token ab, wird er mit dem Refresh-Token automatisch erneuert.</summary>
    public async Task<bool> EnsureValidSessionAsync()
    {
        if (Session is null) return false;
        if (IsLoggedIn) return true;
        var refreshed = await RefreshAsync();
        return refreshed is not null && IsLoggedIn;
    }

    // ---------------- Auth ----------------

    public async Task<(SupabaseSession? session, string? error)> SignUpWithPasswordAsync(string email, string password)
        => await AuthRequestAsync("signup", new { email, password });

    public async Task<(SupabaseSession? session, string? error)> SignInWithPasswordAsync(string email, string password)
        => await AuthRequestAsync("token?grant_type=password", new { email, password });

    private async Task<(SupabaseSession?, string?)> AuthRequestAsync(string path, object body)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/auth/v1/{path}");
            AddApiKey(req);
            req.Content = JsonContent(body);
            var resp = await _http.SendAsync(req);
            string text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (null, ExtractError(text) ?? resp.StatusCode.ToString());

            var json = JsonNode.Parse(text)!.AsObject();
            if (json.ContainsKey("session"))
                json = json["session"]!.AsObject();

            var session = ParseSession(json);

            // Bei aktivierter E-Mail-Bestätigung liefert Supabase zwar einen
            // Benutzer, aber noch keine Sitzung (leerer access_token). Eine
            // solche "Sitzung" darf nicht gespeichert werden, sonst meldet die
            // App fälschlich einen erfolgreichen Login.
            if (string.IsNullOrWhiteSpace(session.AccessToken))
                return (session, null);

            SetSession(session);
            return (session, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<SupabaseSession?> RefreshAsync()
    {
        if (Session is null || string.IsNullOrEmpty(Session.RefreshToken))
            return null;
        var (s, _) = await AuthRequestAsync("token?grant_type=refresh_token",
            new { refresh_token = Session.RefreshToken });
        if (s is not null) SetSession(s);
        return s;
    }

    public async Task SignOutAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/auth/v1/logout");
            AddApiKey(req); AddBearer(req);
            await _http.SendAsync(req);
        }
        catch { /* ignore */ }
        SetSession(null);
    }

    public async Task<string?> GetUserEmailAsync()
    {
        if (Session is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Url}/auth/v1/user");
            AddApiKey(req); AddBearer(req);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!.AsObject();
            return json["email"]?.GetValue<string>();
        }
        catch { return null; }
    }

    // ---------------- PKCE OAuth (genutzt z. B. für Discord) ----------------

    public static (string verifier, string challenge) CreatePkce()
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public string BuildOAuthUrl(string provider, string redirectTo, string codeChallenge)
    {
        string encodedRedirect = Uri.EscapeDataString(redirectTo);
        return $"{Url}/auth/v1/authorize?provider={provider}" +
               $"&redirect_to={encodedRedirect}" +
               $"&code_challenge={codeChallenge}&code_challenge_method=s256";
    }

    public async Task<(SupabaseSession?, string?)> ExchangePkceAsync(string authCode, string codeVerifier)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/auth/v1/token?grant_type=pkce");
            AddApiKey(req);
            req.Content = JsonContent(new { auth_code = authCode, code_verifier = codeVerifier });
            var resp = await _http.SendAsync(req);
            string text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (null, ExtractError(text));

            var json = JsonNode.Parse(text)!.AsObject();
            var session = ParseSession(json);
            SetSession(session);
            return (session, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    // ---------------- REST (PostgREST) ----------------

    public async Task<RestResult> InsertAsync(string table, object row)
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/rest/v1/{table}");
        AddApiKey(req); AddBearer(req); AddProfile(req);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = JsonContent(row);
        var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return new RestResult(resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? text : (ExtractError(text) ?? text));
    }

    public async Task<RestResult> SelectAsync(string table, string query = "select=*")
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Url}/rest/v1/{table}?{query}");
        AddApiKey(req); AddBearer(req); AddProfile(req);
        var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return new RestResult(resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? text : (ExtractError(text) ?? text));
    }

    /// <summary>Zählt Zeilen einer Tabelle (PostgREST <c>count=exact</c>), ohne
    /// alle Datensätze zu übertragen. Nutzt den <c>Content-Range</c>-Header.</summary>
    public async Task<(bool ok, long count)> CountAsync(string table, string filters = "")
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        string query = string.IsNullOrWhiteSpace(filters) ? string.Empty : "?" + filters.TrimStart('?');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Url}/rest/v1/{table}{query}");
        AddApiKey(req); AddBearer(req); AddProfile(req);
        req.Headers.TryAddWithoutValidation("Prefer", "count=exact");
        req.Headers.TryAddWithoutValidation("Range-Unit", "items");
        req.Headers.TryAddWithoutValidation("Range", "0-0");

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return (false, 0);

        string? range = null;
        if (resp.Content.Headers.TryGetValues("Content-Range", out var values))
            range = values.FirstOrDefault();
        if (range is null && resp.Headers.TryGetValues("Content-Range", out var headerValues))
            range = headerValues.FirstOrDefault();

        if (range is null)
            return (false, 0);

        int slash = range.LastIndexOf('/');
        return slash >= 0 && long.TryParse(range[(slash + 1)..], out long count)
            ? (true, count)
            : (false, 0);
    }

    public async Task<RestResult> PatchAsync(string table, string query, object patch)
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{Url}/rest/v1/{table}?{query}");
        AddApiKey(req); AddBearer(req); AddProfile(req);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        req.Content = JsonContent(patch);
        var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return new RestResult(resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? text : (ExtractError(text) ?? text));
    }

    public async Task<RestResult> DeleteAsync(string table, string query)
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{Url}/rest/v1/{table}?{query}");
        AddApiKey(req); AddBearer(req); AddProfile(req);
        var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return new RestResult(resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? text : (ExtractError(text) ?? text));
    }

    /// <summary>Calls a Postgres function exposed via PostgREST RPC.</summary>
    public async Task<RestResult> RpcAsync(string function, object args)
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/rest/v1/rpc/{function}");
        AddApiKey(req); AddBearer(req); AddProfile(req);
        req.Content = JsonContent(args);
        var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return new RestResult(resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? text : (ExtractError(text) ?? text));
    }

    /// <summary>Uploads bytes to a public Supabase Storage bucket.</summary>
    public async Task<RestResult> UploadAsync(string bucket, string path, byte[] data, string contentType)
    {
        if (Session is not null && !IsLoggedIn)
            await RefreshAsync();

        string encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/storage/v1/object/{bucket}/{encodedPath}");
        AddApiKey(req); AddBearer(req);
        req.Headers.TryAddWithoutValidation("x-upsert", "true");
        req.Content = new ByteArrayContent(data);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return new RestResult(resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? text : (ExtractError(text) ?? text));
    }

    public string PublicStorageUrl(string bucket, string path)
        => $"{Url}/storage/v1/object/public/{bucket}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

    // ---------------- Helpers ----------------

    private void AddApiKey(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("apikey", AnonKey);
    }

    /// <summary>
    /// Das Supabase-Projekt kann mehrere Schemas freigeben; ohne diesen Header
    /// waehlt PostgREST das zuletzt konfigurierte Schema (hier "api"). Unsere
    /// Tabellen und RPC-Funktionen liegen in "public".
    /// </summary>
    private static void AddProfile(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("Accept-Profile", "public");
        req.Headers.TryAddWithoutValidation("Content-Profile", "public");
    }

    private void AddBearer(HttpRequestMessage req)
    {
        string token = Session?.AccessToken is { Length: > 0 } t ? t : AnonKey;
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static HttpContent JsonContent(object body)
        => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static SupabaseSession ParseSession(JsonObject json)
    {
        var user = json["user"]?.AsObject();
        var meta = user?["user_metadata"]?.AsObject();
        return new SupabaseSession
        {
            AccessToken = json["access_token"]?.GetValue<string>() ?? string.Empty,
            RefreshToken = json["refresh_token"]?.GetValue<string>() ?? string.Empty,
            ExpiresAtUtc = json["expires_at"] is { } exp
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetValue<long>()).UtcDateTime
                : DateTime.UtcNow.AddHours(1),
            User = new SupabaseUser
            {
                Id = user?["id"]?.GetValue<string>() ?? string.Empty,
                Email = user?["email"]?.GetValue<string>(),
                DisplayName = meta?["full_name"]?.GetValue<string>() ?? meta?["name"]?.GetValue<string>(),
                AvatarUrl = meta?["avatar_url"]?.GetValue<string>()
            }
        };
    }

    private static string? ExtractError(string text)
    {
        try
        {
            var json = JsonNode.Parse(text)?.AsObject();
            return json?["error_description"]?.GetValue<string>()
                   ?? json?["msg"]?.GetValue<string>()
                   ?? json?["message"]?.GetValue<string>()
                   ?? json?["error"]?.GetValue<string>();
        }
        catch { return text; }
    }

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
