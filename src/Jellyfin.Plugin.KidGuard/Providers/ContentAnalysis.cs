using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KidGuard.Core;

namespace Jellyfin.Plugin.KidGuard.Providers;
public interface IContentAdvisoryProvider
{
    Task<Advisory?> Get(LibraryItem item, Settings settings, CancellationToken cancellation);
}
public sealed class JellyfinMetadataProvider : IContentAdvisoryProvider
{
    public Task<Advisory?> Get(LibraryItem item, Settings settings, CancellationToken cancellation)
    {
        var dimensions = new Dictionary<Dimension, int>();
        // Only explicit administrator-authored structured tags. Never infer scene facts from a synopsis.
        foreach (var tag in item.Tags)
        {
            var parts = tag.Split(':');
            if (parts.Length == 3 && parts[0].Equals("Advisory", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<Dimension>(parts[1], true, out var dimension) && Enum.IsDefined(dimension)
                && int.TryParse(parts[2], out var severity) && severity is >= 0 and <= 4)
                dimensions[dimension] = Math.Max(dimensions.GetValueOrDefault(dimension), severity);
        }
        return Task.FromResult<Advisory?>(new("Jellyfin metadata", item.Rating, dimensions, "Certification and explicit Advisory:Dimension:0–4 metadata tags. Other dimensions are unknown."));
    }
}
public sealed class TmdbProvider : IContentAdvisoryProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _rate = new(1, 1);
    public TmdbProvider() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public TmdbProvider(HttpMessageHandler handler) { _http = new(handler) { Timeout = TimeSpan.FromSeconds(15) }; }
    public async Task<Advisory?> Get(LibraryItem item, Settings settings, CancellationToken cancellation)
    {
        if (!settings.ExternalEnabled || string.IsNullOrWhiteSpace(settings.TmdbToken) || item.Kind is MediaKind.Episode or MediaKind.Season) return null;
        if (!item.ProviderIds.TryGetValue("Tmdb", out var id) || !long.TryParse(id, out var number) || number <= 0) return null;
        await _rate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var path = item.Kind == MediaKind.Movie ? $"movie/{number}/release_dates" : $"tv/{number}/content_ratings";
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/" + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.TmdbToken);
                using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellation).ConfigureAwait(false); continue; }
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
                var ratings = new List<string>();
                foreach (var country in document.RootElement.GetProperty("results").EnumerateArray())
                {
                    if (country.GetProperty("iso_3166_1").GetString() != settings.Country) continue;
                    if (item.Kind == MediaKind.Movie)
                        ratings.AddRange(country.GetProperty("release_dates").EnumerateArray().Select(r => r.GetProperty("certification").GetString() ?? ""));
                    else ratings.Add(country.GetProperty("rating").GetString() ?? "");
                }
                var valid = ratings.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().Select(r => settings.Country == "US" ? r : settings.Country + "-" + r).ToArray();
                if (valid.Length == 0) return new("TMDb certification", null, [], "No certification for the configured country.", true);
                if (valid.Select(Ratings.Age).Where(a => a.HasValue).Distinct().Count() > 1)
                    return new("TMDb certification", null, [], "Multiple release certifications require parent review.", true);
                return new("TMDb certification", valid[0], [], "Certification only. TMDb did not supply scene-level advisories.");
            }
            return new("TMDb certification", null, [], "Provider retry limit reached.", true);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException || e is OperationCanceledException && !cancellation.IsCancellationRequested)
        { return new("TMDb certification", null, [], "Provider unavailable or invalid response; cached failure expires after 10 minutes.", true); }
        finally { try { await Task.Delay(260, cancellation).ConfigureAwait(false); } finally { _rate.Release(); } }
    }
    public void Dispose() { _http.Dispose(); _rate.Dispose(); }
}
public sealed class ContentAnalysis : IDisposable
{
    private readonly JellyfinMetadataProvider _local = new();
    private readonly TmdbProvider _tmdb = new();
    public static string Fingerprint(LibraryItem item, Settings settings)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(new { item.Id, item.Rating, item.ProviderIds, Tags = item.Tags.Where(t => !t.StartsWith("KidGuard:", StringComparison.OrdinalIgnoreCase) && t != "KidSafe").Order(), settings.Country, settings.ExternalEnabled, TokenConfigured = settings.TmdbToken.Length > 0 });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    public async Task<Assessment> Analyze(LibraryItem item, Settings settings, Assessment? cached, CancellationToken cancellation)
    {
        var fingerprint = Fingerprint(item, settings);
        var ttl = cached?.Evidence.Any(e => e.Failed) == true ? TimeSpan.FromMinutes(10) : TimeSpan.FromDays(settings.CacheDays);
        if (cached is not null && cached.Version == 1 && cached.Fingerprint == fingerprint && DateTimeOffset.UtcNow - cached.RetrievedAt < ttl)
            return cached with { Item = item };
        var evidence = new List<Advisory> { (await _local.Get(item, settings, cancellation).ConfigureAwait(false))! };
        var external = await _tmdb.Get(item, settings, cancellation).ConfigureAwait(false);
        if (external is not null) evidence.Add(external);
        return new(item, evidence, DateTimeOffset.UtcNow, fingerprint);
    }
    public void Dispose() => _tmdb.Dispose();
}
