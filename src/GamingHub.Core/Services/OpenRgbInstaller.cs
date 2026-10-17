using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace GamingHub.Core.Services;

/// <summary>
/// Lädt OpenRGB automatisch von der offiziellen GitHub-Release-Seite
/// (OpenRGB ist ein separates Open-Source-Projekt, GPL-2.0) und hält es
/// aktuell. Die Binaries werden in einem eigenen Ordner abgelegt und von
/// <see cref="RgbService"/> gestartet.
/// </summary>
public sealed class OpenRgbInstaller
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/CalcProgrammer1/OpenRGB/releases/latest";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly string _installDir;
    private readonly string _versionFile;

    public string InstallDirectory => _installDir;
    public string? ExecutablePath { get; private set; }
    public string? InstalledVersion { get; private set; }

    public bool IsInstalled => ExecutablePath != null;

    public OpenRgbInstaller(string baseDirectory)
    {
        _installDir = Path.Combine(baseDirectory, "OpenRGB");
        _versionFile = Path.Combine(_installDir, "version.txt");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GamingHub/1.0 (+https://github.com/jggaming25/Gaming-Hub)");
        RefreshState();
    }

    private void RefreshState()
    {
        try
        {
            ExecutablePath = Directory.Exists(_installDir)
                ? Directory.EnumerateFiles(_installDir, "OpenRGB.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }
        catch
        {
            ExecutablePath = null;
        }

        InstalledVersion = File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;
    }

    /// <summary>Stellt sicher, dass OpenRGB vorhanden und aktuell ist.</summary>
    public async Task<bool> EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        RefreshState();

        var release = await FetchLatestReleaseAsync(ct).ConfigureAwait(false);
        if (release is null)
            return IsInstalled; // offline: vorhandene Installation weiterverwenden

        if (IsInstalled && string.Equals(InstalledVersion, release.Value.Version, StringComparison.OrdinalIgnoreCase))
            return true;

        progress?.Report($"Lade OpenRGB {FormatVersion(release.Value.Version)} herunter…");
        return await DownloadAndInstallAsync(release.Value.Version, release.Value.Url, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Aktualisiert eine vorhandene Installation im Hintergrund (ohne Download, falls nichts installiert ist).</summary>
    public async Task UpdateIfInstalledAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        RefreshState();
        if (!IsInstalled) return;

        try
        {
            var release = await FetchLatestReleaseAsync(ct).ConfigureAwait(false);
            if (release is null) return;
            if (string.Equals(InstalledVersion, release.Value.Version, StringComparison.OrdinalIgnoreCase)) return;

            progress?.Report($"Aktualisiere OpenRGB auf {FormatVersion(release.Value.Version)}…");
            await DownloadAndInstallAsync(release.Value.Version, release.Value.Url, progress, ct).ConfigureAwait(false);
        }
        catch
        {
            // Update ist optional – Fehler dürfen den Start nicht stören.
        }
    }

    private async Task<(string Version, string Url)?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = json.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
            var version = tagEl.GetString();
            if (string.IsNullOrWhiteSpace(version)) return null;

            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.Contains("Windows_64", StringComparison.OrdinalIgnoreCase)) continue;

                    url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
                    break;
                }
            }

            return url is null ? null : (version, url);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> DownloadAndInstallAsync(string version, string url, IProgress<string>? progress, CancellationToken ct)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"gaminghub-openrgb-{Guid.NewGuid():N}.zip");
        var stagingDir = _installDir + ".new";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_installDir)!);

            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(zipPath);

                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    if (total is > 0)
                        progress?.Report($"Lade OpenRGB herunter… {received * 100 / total.Value}%");
                }
            }

            progress?.Report("Entpacke OpenRGB…");
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(zipPath, stagingDir, true);

            try
            {
                if (Directory.Exists(_installDir)) Directory.Delete(_installDir, true);
                Directory.Move(stagingDir, _installDir);
                Directory.CreateDirectory(_installDir);
                File.WriteAllText(_versionFile, version);
            }
            catch
            {
                try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                return IsInstalled;
            }

            RefreshState();
            progress?.Report($"OpenRGB {FormatVersion(version)} bereit.");
            return IsInstalled;
        }
        catch
        {
            return IsInstalled;
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    private static string FormatVersion(string version) =>
        version.StartsWith("release_", StringComparison.OrdinalIgnoreCase) ? version["release_".Length..] : version;
}
