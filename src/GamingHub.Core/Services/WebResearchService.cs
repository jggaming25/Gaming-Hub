using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingHub.Core.Services;

public sealed record WebResearchSource(string Title, string Url, string Snippet);

public sealed class WebResearchResult
{
    public string Query { get; set; } = string.Empty;

    /// <summary>Vom Aufrufer mitgelieferte Versions-Präzisierung (z. B. „2.1“).</summary>
    public string? QueryVersion { get; set; }

    /// <summary>In den Ergebnissen erkannte Version, auf die sich die Angaben
    /// beziehen (falls erkennbar) – für die Anzeige „bezieht sich auf Version X“.</summary>
    public string? DetectedVersion { get; set; }

    /// <summary>Fertiger Hinweistext zur Versions-Einordnung, null wenn unbekannt.</summary>
    public string? VersionNote { get; set; }

    public string Summary { get; set; } = string.Empty;
    public List<WebResearchSource> Sources { get; } = new();
    public string? Error { get; set; }
    public bool IsEstimate { get; set; }

    public bool Found => !string.IsNullOrWhiteSpace(Summary) || Sources.Count > 0;
}

/// <summary>
/// Background web lookup for "Systemanforderungen" of a game. Uses Wikipedia
/// (de + en) with a title-relevance scoring, extracts the requirements section
/// and falls back to DuckDuckGo HTML scraping when Wikipedia has no article.
/// No API key required.
/// </summary>
public sealed class WebResearchService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GamingHub/1.0 (+https://jggaming25.github.io/Gaming-Hub/)");
        return client;
    }

    public async Task<WebResearchResult> ResearchAsync(string query, string? versionHint = null, CancellationToken ct = default)
    {
        var result = new WebResearchResult { Query = query, QueryVersion = NormalizeVersion(versionHint) };
        if (string.IsNullOrWhiteSpace(query))
            return result;

        string game = ExtractGameName(query);

        // Versions-Präzisierung: Spielname und Version werden in die Suche
        // mitgegeben, damit die Ergebnisse zur installierten Version passen.
        string search = string.IsNullOrWhiteSpace(result.QueryVersion)
            ? game
            : $"{game} {result.QueryVersion}";

        await TryWikipediaAsync(search, game, result, ct).ConfigureAwait(false);
        if (!result.Found)
            await TryDuckDuckGoAsync(search, result, ct).ConfigureAwait(false);

        result.DetectedVersion = DetectVersion(result.Summary);
        if (!string.IsNullOrWhiteSpace(result.DetectedVersion))
        {
            result.VersionNote = string.IsNullOrWhiteSpace(result.QueryVersion)
                ? $"Hinweis: Die Angaben beziehen sich auf Version {result.DetectedVersion}."
                : $"Hinweis: Die Angaben beziehen sich auf Version {result.DetectedVersion} (gesucht: {result.QueryVersion}).";
        }

        return result;
    }

    private static string NormalizeVersion(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return string.Empty;
        string t = hint.Trim();
        t = Regex.Replace(t, @"^(version|v|v\.|ver\.)\s*", "", RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>Erkennt eine Versionsangabe im Ergebnistext ("Version 2.1",
    /// "Patch 1.05", "v2024.1" o. ä.) für die Versions-Einordnung.</summary>
    private static string? DetectVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] patterns =
        {
            @"(?i)version\s+(\d+(?:\.\d+){1,4})",
            @"(?i)(?:patch|update)\s+(\d+(?:\.\d+){1,4})",
            @"(?i)\bv(\d+(?:\.\d+){1,4})\b"
        };
        foreach (string pattern in patterns)
        {
            var m = Regex.Match(text, pattern);
            if (m.Success && m.Groups.Count > 1)
                return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Strips suffixes such as "Systemanforderungen"/"system requirements".</summary>
    private static string ExtractGameName(string query)
    {
        string q = Regex.Replace(query, @"\s+", " ").Trim();
        q = Regex.Replace(q, @"\s*(systemanforderungen|system requirements|systemvoraussetzungen|requirements)\s*$", "", RegexOptions.IgnoreCase);
        return q.Trim();
    }

    private static async Task TryWikipediaAsync(string search, string game, WebResearchResult result, CancellationToken ct)
    {
        try
        {
            var candidates = await SearchWikiAsync("de", search, 6, ct).ConfigureAwait(false);
            if (candidates.Count == 0)
                candidates = await SearchWikiAsync("en", search, 6, ct).ConfigureAwait(false);

            // Relevance: require at least one significant token of the game name
            // to appear in the candidate title; single-token names must be contained.
            var tokens = SignificantTokens(game);
            var relevant = candidates
                .Where(c => IsRelevant(c.Title, tokens, game))
                .ToList();

            if (relevant.Count == 0)
                return;

            var best = relevant[0];
            string snippetTitle = TitleWithoutTemplate(best.Title);
            var (extract, url) = await FetchExtractAsync("de", best.Title, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(extract))
                (extract, url) = await FetchExtractAsync("en", best.Title, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(extract))
                return;

            result.Error = null;
            string requirements = ExtractRequirementsSection(extract);

            if (!string.IsNullOrWhiteSpace(requirements))
            {
                result.Summary = requirements;
                result.IsEstimate = false;
            }
            else
            {
                result.Summary = Truncate(extract, 1400);
                result.IsEstimate = true;
            }

            result.Sources.Add(new WebResearchSource(snippetTitle, string.IsNullOrWhiteSpace(url) ? WikiUrl("de", best.Title) : url, Truncate(extract, 260)));

            foreach (var extra in relevant.Skip(1).Take(2))
            {
                var (e2, u2) = await FetchExtractAsync("de", extra.Title, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(e2))
                    (e2, u2) = await FetchExtractAsync("en", extra.Title, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(e2))
                    continue;
                result.Sources.Add(new WebResearchSource(TitleWithoutTemplate(extra.Title),
                    string.IsNullOrWhiteSpace(u2) ? WikiUrl("de", extra.Title) : u2, Truncate(e2, 260)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
    }

    private static async Task<List<(string Title, string Snippet)>> SearchWikiAsync(string lang, string game, int limit, CancellationToken ct)
    {
        var list = new List<(string Title, string Snippet)>();

        string searchUrl = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit={limit}&srsearch=" +
                           Uri.EscapeDataString(game);
        using var resp = await Http.GetAsync(searchUrl, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("query", out var q) ||
            !q.TryGetProperty("search", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            string title = GetString(item, "title");
            if (!string.IsNullOrWhiteSpace(title))
                list.Add((title, GetString(item, "snippet")));
        }

        return list;
    }

    private static async Task<(string Extract, string PageUrl)> FetchExtractAsync(string lang, string title, CancellationToken ct)
    {
        string extractUrl = "https://" + lang + ".wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&format=json&redirects=1&exintro=0&titles=" +
                            Uri.EscapeDataString(title.Replace(' ', '_'));
        using var resp = await Http.GetAsync(extractUrl, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));

        string extract = string.Empty;
        if (doc.RootElement.TryGetProperty("query", out var q) &&
            q.TryGetProperty("pages", out var pages))
        {
            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("extract", out var ex) || ex.ValueKind != JsonValueKind.String)
                    continue;
                extract = ex.GetString() ?? string.Empty;
                break;
            }
        }

        string pageUrl = "https://" + lang + ".wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
        return (extract, pageUrl);
    }

    private static string WikiUrl(string lang, string title)
        => "https://" + lang + ".wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));

    private static List<string> SignificantTokens(string game)
    {
        return Regex.Split(game.ToLowerInvariant(), @"[^a-z0-9\u00e4\u00f6\u00fc\u00df]+")
            .Where(t => t.Length >= 3 && !StopWords.Contains(t))
            .Distinct()
            .ToList();
    }

    private static readonly HashSet<string> StopWords = new()
    {
        "the", "and", "der", "die", "das", "den", "dem", "des", "ein", "eine", "einer", "eines", "mit",
        "von", "vom", "auf", "für", "fuer", "und", "oder", "im", "in", "for", "edition", "game", "spiel"
    };

    private static bool IsRelevant(string title, List<string> tokens, string game)
    {
        string t = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9\u00e4\u00f6\u00fc\u00df]", string.Empty);
        if (tokens.Count == 0)
            return false;

        if (tokens.Count == 1)
            return t.Contains(tokens[0]);

        int hits = tokens.Count(tok => t.Contains(tok));
        return hits >= 1;
    }

    private static string ExtractRequirementsSection(string extract)
    {
        string[] markers =
        {
            "systemanforderungen", "system requirements", "systemvoraussetzungen",
            "systemanforderung", "mindestanforderungen", "minimum requirements"
        };

        int best = -1;
        foreach (var m in markers)
        {
            int i = extract.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (best == -1 || i < best))
                best = i;
        }

        if (best < 0)
            return string.Empty;

        string section = extract[best..];
        int end = section.Length;
        int nextHeading = section.IndexOf("\n==", 8);
        if (nextHeading > 0)
            end = Math.Min(end, nextHeading);
        int wz = section.IndexOf("[bearbeiten]", StringComparison.OrdinalIgnoreCase);
        if (wz > 0 && end == section.Length)
            end = Math.Min(end, wz);
        if (end > 3200)
            end = 3200;

        return Truncate(section[..end].Trim(), 2400);
    }

    private static async Task TryDuckDuckGoAsync(string search, WebResearchResult result, CancellationToken ct)
    {
        try
        {
            // DuckDuckGo HTML page – parse result links. This is a fallback only.
            string url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(search + " system requirements");
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var links = new List<(string Title, string Url, string Snippet)>();
            foreach (Match m in Regex.Matches(html, @"<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                string title = StripTags(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value));
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                    continue;
                string snippet = string.Empty;
                var s = Regex.Match(html[m.Index..], @"class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (s.Success)
                    snippet = StripTags(System.Net.WebUtility.HtmlDecode(s.Groups[1].Value));
                links.Add((title, href, snippet));
            }

            if (links.Count > 0)
            {
                result.Summary = Truncate(links[0].Title + " – " + links[0].Snippet, 1400);
                result.IsEstimate = true;
                foreach (var l in links.Take(6))
                    result.Sources.Add(new WebResearchSource(l.Title, l.Url, string.IsNullOrWhiteSpace(l.Snippet) ? l.Url : l.Snippet));
                result.Error = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
    }

    private static string TitleWithoutTemplate(string title)
        => Regex.Replace(title, @"[\[{].*?[\]}]", "").Trim();

    private static string StripTags(string html) => Regex.Replace(html, "<.*?>", string.Empty).Trim();

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + " …";

    private static string GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}