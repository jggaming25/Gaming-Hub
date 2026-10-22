using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamingHub.Core.Services;

namespace GamingHub.App.Services;

/// <summary>
/// Handles Supabase authentication (e-mail/password and Discord OAuth via
/// a loopback listener) and keeps the session persisted between starts.
/// </summary>
public sealed class AuthService
{
    private const int OAuthPort = 51555;
    private readonly string _sessionPath;
    private readonly Timer _refreshTimer;

    public SupabaseClient? Client { get; private set; }
    public bool IsConfigured => Client is not null;
    public SupabaseUser? CurrentUser => Client?.Session?.User;
    public bool IsLoggedIn => Client?.IsLoggedIn ?? false;

    public event Action? Changed;

    public AuthService(string dataDirectory)
    {
        _sessionPath = Path.Combine(dataDirectory, "session.json");

        var (url, key) = ResolveConfig();
        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key))
        {
            Client = new SupabaseClient(url, key);
            Client.SessionChanged += PersistSession;
            RestorePersistedSession();
        }

        // Sitzung beim Start erneuern (der Access-Token gilt nur ~1 Stunde) und
        // danach regelmäßig gültig halten. Sonst meldet die App nach dem Ablauf
        // fälschlich „nicht angemeldet“, obwohl ein Refresh-Token vorliegt.
        _ = EnsureSessionAsync();
        _refreshTimer = new Timer(_ => _ = EnsureSessionAsync(), null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    /// <summary>Erneuert die gespeicherte Sitzung bei Bedarf. Gibt zurück, ob
    /// danach eine gültige Anmeldung vorliegt.</summary>
    public async Task<bool> EnsureSessionAsync()
    {
        if (Client is null) return false;
        bool before = IsLoggedIn;
        bool ok = await Client.EnsureValidSessionAsync();
        if (ok != before) Changed?.Invoke();
        return ok;
    }

    public void Stop() => _refreshTimer.Dispose();

    private (string? url, string? key) ResolveConfig()
    {
        // 1) Vom Nutzer gespeicherte Werte haben Vorrang.
        var s = AppServices.Settings.Settings;
        string? url = string.IsNullOrWhiteSpace(s.SupabaseUrl) ? null : s.SupabaseUrl;
        string? key = string.IsNullOrWhiteSpace(s.SupabaseAnonKey) ? null : s.SupabaseAnonKey;

        // 2) Optionales Override per Datei neben der Exe.
        if (url is null || key is null)
        {
            try
            {
                string configPath = Path.Combine(AppContext.BaseDirectory, "supabase.config.json");
                if (File.Exists(configPath))
                {
                    var json = JsonNode.Parse(File.ReadAllText(configPath));
                    url ??= json?["url"]?.GetValue<string>();
                    key ??= json?["anonKey"]?.GetValue<string>();
                }
            }
            catch { /* ignore */ }
        }

        // 3) Fest hinterlegte Standardwerte -> funktioniert sofort für jeden.
        url ??= SupabaseDefaults.Url;
        key ??= SupabaseDefaults.AnonKey;

        return (url, key);
    }

    public string GetOAuthRedirectUrl() => $"http://localhost:{OAuthPort}/callback";

    public async Task<(bool ok, bool confirmationRequired, string? error)> SignUpAsync(string email, string password)
    {
        if (Client is null) return (false, false, "Supabase ist nicht konfiguriert.");
        var (session, error) = await Client.SignUpWithPasswordAsync(email, password);

        // Supabase gibt bei aktivierter E-Mail-Bestätigung einen Benutzer ohne
        // access_token zurück. Das ist kein Fehler, aber auch noch keine
        // Anmeldung – die UI muss das klar unterscheiden.
        bool confirmationRequired = session is not null &&
                                    string.IsNullOrWhiteSpace(session.AccessToken);

        if (session is not null && !confirmationRequired)
            PersistSession();

        Changed?.Invoke();
        return (session is not null && !confirmationRequired, confirmationRequired, error);
    }

    public async Task<(bool ok, string? error)> SignInAsync(string email, string password)
    {
        if (Client is null) return (false, "Supabase ist nicht konfiguriert.");
        var (session, error) = await Client.SignInWithPasswordAsync(email, password);
        if (session is not null) PersistSession();
        Changed?.Invoke();
        return (session is not null, error);
    }

    public async Task SignOutAsync()
    {
        if (Client is null) return;
        await Client.SignOutAsync();
        TryDelete(_sessionPath);
        Changed?.Invoke();
    }

    /// <summary>Opens the browser for OAuth and waits for the loopback callback.</summary>
    public async Task<(bool ok, string? error)> SignInWithOAuthAsync(string provider)
    {
        if (Client is null) return (false, "Supabase ist nicht konfiguriert.");

        string localRedirect = GetOAuthRedirectUrl();
        // Viele Supabase-Projekte erlauben nur die Website als Redirect. Die
        // Website leitet den Code dann an den lokalen Loopback-Port weiter.
        string relayRedirect = AppInfo.WebsiteUrl + "?gh_oauth=1";

        string? timeoutMessage = null;
        foreach (string redirect in new[] { relayRedirect, localRedirect })
        {
            var (verifier, challenge) = SupabaseClient.CreatePkce();
            string url = Client.BuildOAuthUrl(provider, redirect, challenge);

            TcpListener? listener = StartLoopbackListener(out string? listenError);
            if (listener is null)
                return (false, listenError ?? "Loopback-Port belegt.");

            using (listener)
            {
                OpenBrowser(url);

                string? code = null;
                string? error = null;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
                    using var stream = tcp.GetStream();
                    string requestLine = await ReadRequestLineAsync(stream);
                    (code, error) = ParseCallback(requestLine);
                    await WriteResponseAsync(stream, error is null);
                }
                catch (OperationCanceledException)
                {
                    timeoutMessage =
                        "Zeitüberschreitung bei der Discord-Anmeldung. Erlaubte Redirect-URLs " +
                        $"im Supabase-Projekt prüfen: {relayRedirect} und {localRedirect}.";
                    continue;
                }
                catch (Exception ex)
                {
                    LogService.Warn("Discord-OAuth fehlgeschlagen.", ex);
                    return (false, "OAuth abgebrochen: " + ex.Message);
                }

                if (error is not null) return (false, error);
                if (string.IsNullOrEmpty(code)) continue;

                var (session, exchangeError) = await Client.ExchangePkceAsync(code, verifier);
                if (session is not null) PersistSession();
                Changed?.Invoke();
                return (session is not null, exchangeError);
            }
        }

        return (false, timeoutMessage ?? "Kein Autorisierungs-Code erhalten.");
    }

    private static TcpListener? StartLoopbackListener(out string? error)
    {
        // Windows löst „localhost“ oft zuerst auf IPv6 (::1) auf. Ein reiner
        // IPv4-Listener führte dann zu einem Verbindungsfehler im Browser.
        // Dual-Stack versucht beides; bei fehlendem IPv6 bleibt IPv4 als Fallback.
        try
        {
            var listener = new TcpListener(IPAddress.IPv6Any, OAuthPort);
            listener.Server.DualMode = true;
            listener.Start();
            error = null;
            return listener;
        }
        catch
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, OAuthPort);
                listener.Start();
                error = null;
                return listener;
            }
            catch (Exception ex)
            {
                error = "Loopback-Port belegt: " + ex.Message;
                return null;
            }
        }
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (read == 0) break;
            total += read;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
        }
        string text = Encoding.ASCII.GetString(buffer, 0, total);
        return text.Split("\r\n")[0];
    }

    private static (string? code, string? error) ParseCallback(string requestLine)
    {
        try
        {
            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return (null, "Ungültige Anfrage");
            var uri = new Uri("http://localhost" + parts[1]);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return (query["code"], query["error_description"] ?? query["error"]);
        }
        catch
        {
            return (null, "Callback konnte nicht gelesen werden");
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, bool success)
    {
        string message = success
            ? "<h2>Anmeldung erfolgreich</h2><p>Du kannst dieses Fenster schließen und zu Gaming Hub zurückkehren.</p>"
            : "<h2>Anmeldung fehlgeschlagen</h2><p>Bitte versuche es in Gaming Hub erneut.</p>";
        string body = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Gaming Hub</title></head>" +
                      $"<body style=\"font-family:sans-serif;background:#0B0D1A;color:#E5E7EB;text-align:center;padding-top:80px\">{message}</body></html>";
        byte[] content = Encoding.UTF8.GetBytes(body);
        string header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                        $"Content-Length: {content.Length}\r\nConnection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(content);
        await stream.FlushAsync();
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private void PersistSession()
    {
        try
        {
            if (Client?.Session is null) return;

            byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Client.Session));
            byte[]? encrypted = Dpapi.Protect(json);
            File.WriteAllBytes(_sessionPath, encrypted ?? json);
        }
        catch { /* ignore */ }
    }

    private void RestorePersistedSession()
    {
        try
        {
            if (!File.Exists(_sessionPath) || Client is null) return;

            byte[] raw = File.ReadAllBytes(_sessionPath);
            byte[]? decrypted = Dpapi.Unprotect(raw);
            string json = Encoding.UTF8.GetString(decrypted ?? raw);
            var session = JsonSerializer.Deserialize<SupabaseSession>(json);
            Client.RestoreSession(session);
        }
        catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
