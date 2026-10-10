using System.Net.Http;
using System.Text.Json;

namespace GamingHub.Core.Services;

public sealed record WebResearchSource(string Title, string Url, string Snippet);

public sealed class WebResearchResult
{
    public string Query { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<WebResearchSource> Sources { get; } = new();
    public string? Error { get; set; }

    public bool Found => !string.IsNullOrWhiteSpace(Summary) || Sources.Count > 0;
}

/// <summary>
/// Runs a lightweight background web lookup (DuckDuckGo Instant Answer API with a
/// Wikipedia fallback) so the result can be shown inside the app instead of only
/// opening a browser. No API key required.
/// </summary>
public sealed class WebResearchService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GamingHub/1.0 (+https://jggaming25.github.io/Gaming-Hub/)");
        return client;
    }

    public async Task<WebResearchResult> ResearchAsync(string query, CancellationToken ct = default)
    {
        var result = new WebResearchResult { Query = query };
        if (string.IsNullOrWhiteSpace(query))
            return result;

        await TryDuckDuckGoAsync(query, result, ct).ConfigureAwait(false);
        if (!result.Found && result.Error is null)
            await TryWikipediaAsync(query, result, ct).ConfigureAwait(false);

        return result;
    }

    private static async Task TryDuckDuckGoAsync(string query, WebResearchResult result, CancellationToken ct)
    {
        try
        {
            string url = "https://api.duckduckgo.com/?q=" + Uri.EscapeDataString(query) +
                         "&format=json&no_html=1&skip_disambig=1";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;

            string abstractText = GetString(root, "AbstractText");
            string abstractUrl = GetString(root, "AbstractURL");
            string heading = GetString(root, "Heading");

            if (!string.IsNullOrWhiteSpace(abstractText))
            {
                result.Summary = abstractText;
                if (!string.IsNullOrWhiteSpace(abstractUrl))
                    result.Sources.Add(new WebResearchSource(string.IsNullOrWhiteSpace(heading) ? "DuckDuckGo" : heading, abstractUrl, abstractText));
            }

            if (root.TryGetProperty("RelatedTopics", out var topics) && topics.ValueKind == JsonValueKind.Array)
            {
                foreach (var topic in topics.EnumerateArray())
                {
                    if (result.Sources.Count >= 6)
                        break;
                    if (topic.ValueKind != JsonValueKind.Object)
                        continue;
                    string text = GetString(topic, "Text");
                    string firstUrl = GetString(topic, "FirstURL");
                    if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(firstUrl))
                        result.Sources.Add(new WebResearchSource(FirstWords(text), firstUrl, text));
                }
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

    private static async Task TryWikipediaAsync(string query, WebResearchResult result, CancellationToken ct)
    {
        try
        {
            // Wikipedia search API (German), then fetch the top extract.
            string searchUrl = "https://de.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=1&srsearch=" +
                               Uri.EscapeDataString(query);
            using var searchResp = await Http.GetAsync(searchUrl, ct).ConfigureAwait(false);
            searchResp.EnsureSuccessStatusCode();
            using var searchDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!searchDoc.RootElement.TryGetProperty("query", out var q) ||
                !q.TryGetProperty("search", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                return;

            string title = GetString(arr[0], "title");
            if (string.IsNullOrWhiteSpace(title))
                return;

            string extractUrl = "https://de.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title);
            using var extResp = await Http.GetAsync(extractUrl, ct).ConfigureAwait(false);
            extResp.EnsureSuccessStatusCode();
            using var extDoc = JsonDocument.Parse(await extResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var ext = extDoc.RootElement;

            string extract = GetString(ext, "extract");
            string pageUrl = string.Empty;
            if (ext.TryGetProperty("content_urls", out var cu) &&
                cu.TryGetProperty("desktop", out var dt))
                pageUrl = GetString(dt, "page");

            if (!string.IsNullOrWhiteSpace(extract))
            {
                result.Summary = extract;
                result.Sources.Add(new WebResearchSource(title, pageUrl, extract));
                result.Error = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Error ??= ex.Message;
        }
    }

    private static string FirstWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 6 ? text : string.Join(' ', words.Take(6)) + "…";
    }

    private static string GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
