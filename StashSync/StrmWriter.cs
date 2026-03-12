using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.StashSync.Configuration;
using Jellyfin.Plugin.StashSync.GraphQL;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StashSync.Tasks;

/// <summary>
/// Writes .strm + .nfo sidecar pairs for each Stash Group.
///
/// Output layout:
///   {StrmOutputPath}/
///     {GroupName} (StashGroup-{id})/
///       {GroupName}.strm          ← streams the FIRST scene (Jellyfin's anchor file)
///       {GroupName}.nfo           ← movie metadata for Jellyfin
///       chapter-metadata.xml      ← chapter list used by the metadata provider
///       poster.jpg                ← cover art (TMDB preferred, Stash fallback)
/// </summary>
public class StrmWriter
{
    private readonly ILogger<StrmWriter> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private const string TmdbImageBase = "https://image.tmdb.org/t/p/original";
    private const string TmdbApiBase = "https://api.themoviedb.org/3";

    public StrmWriter(ILogger<StrmWriter> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Creates/updates the folder and files for a single Group.
    /// Returns true if anything was written.
    /// </summary>
    public async Task<bool> WriteGroupAsync(
        StashGroup group,
        PluginConfiguration config,
        CancellationToken ct)
    {
        if (group.Scenes.Count < config.MinSceneCount)
        {
            _logger.LogDebug("[StashSync] Skipping group '{Name}' – only {Count} scene(s)", group.Name, group.Scenes.Count);
            return false;
        }

        if (!config.IncludeGroupsWithoutCover && string.IsNullOrEmpty(group.FrontImagePath))
        {
            _logger.LogDebug("[StashSync] Skipping group '{Name}' – no cover image", group.Name);
            return false;
        }

        // Sanitise folder name
        var safeName = SanitiseFileName(group.Name);
        var folderName = $"{safeName} (StashGroup-{group.Id})";
        var folderPath = Path.Combine(config.StrmOutputPath, folderName);

        Directory.CreateDirectory(folderPath);

        // 1. Write the .strm anchor file (first scene stream URL or local path)
        var strmPath = Path.Combine(folderPath, $"{safeName}.strm");
        var streamTarget = ResolveStreamTarget(group.Scenes[0], config);
        if (string.IsNullOrEmpty(streamTarget))
        {
            _logger.LogWarning("[StashSync] Group '{Name}' scene 0 has no resolvable stream — skipping", group.Name);
            return false;
        }
        await File.WriteAllTextAsync(strmPath, streamTarget, Encoding.UTF8, ct).ConfigureAwait(false);

        // 2. Fetch TMDB images if we have a TMDB ID and API key
        var tmdbId = group.Urls.Select(ExtractTmdbId).FirstOrDefault(id => id != null);
        TmdbImages? tmdbImages = null;
        if (tmdbId != null && !string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            tmdbImages = await FetchTmdbImagesAsync(tmdbId, config.TmdbApiKey, ct).ConfigureAwait(false);
        }

        // 3. Write the NFO sidecar (Kodi/Jellyfin movie format)
        var nfoPath = Path.Combine(folderPath, $"{safeName}.nfo");
        WriteNfo(group, tmdbId, tmdbImages, nfoPath);

        // 4. Write chapter-metadata.xml (custom, read by our metadata provider)
        var chaptersPath = Path.Combine(folderPath, "chapter-metadata.xml");
        WriteChapterMetadata(group, config, chaptersPath);

        // 5. Download poster — TMDB preferred, Stash cover as fallback
        var posterPath = Path.Combine(folderPath, "poster.jpg");
        if (tmdbImages?.PosterPath != null)
        {
            var posterUrl = $"{TmdbImageBase}{tmdbImages.PosterPath}";
            await DownloadImageAsync(posterUrl, null, posterPath, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(group.FrontImagePath))
        {
            await DownloadImageAsync(group.FrontImagePath, config, posterPath, ct).ConfigureAwait(false);
        }

        // 6. Download backdrop if available from TMDB
        if (tmdbImages?.BackdropPath != null)
        {
            var backdropPath = Path.Combine(folderPath, "backdrop.jpg");
            var backdropUrl = $"{TmdbImageBase}{tmdbImages.BackdropPath}";
            await DownloadImageAsync(backdropUrl, null, backdropPath, ct).ConfigureAwait(false);
        }

        _logger.LogInformation("[StashSync] Written group '{Name}' → {Folder}", group.Name, folderPath);
        return true;
    }

    /// <summary>
    /// Removes folders for groups that no longer exist in Stash.
    /// </summary>
    public void CleanupOrphanedGroups(IEnumerable<StashGroup> currentGroups, PluginConfiguration config)
    {
        if (!Directory.Exists(config.StrmOutputPath))
            return;

        var validFolderSuffixes = new HashSet<string>(
            currentGroups.Select(g => $"(StashGroup-{g.Id})"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(config.StrmOutputPath))
        {
            var dirName = Path.GetFileName(dir);
            bool isStashFolder = dirName != null &&
                validFolderSuffixes.Any(suffix => dirName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            if (!isStashFolder && dirName != null && dirName.Contains("(StashGroup-", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[StashSync] Removing orphaned group folder: {Dir}", dir);
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private static string ResolveStreamTarget(StashScene scene, PluginConfiguration config)
    {
        if (config.UseStreamUrls)
            return $"{config.StashUrl.TrimEnd('/')}/scene/{scene.Id}/stream";

        return scene.Files.FirstOrDefault()?.Path ?? string.Empty;
    }

    private void WriteNfo(StashGroup group, string? tmdbId, TmdbImages? tmdbImages, string nfoPath)
    {
        double cumulativeSeconds = 0;
        var chapterElements = new List<XElement>();
        foreach (var scene in group.Scenes)
        {
            var duration = scene.Files.FirstOrDefault()?.Duration ?? 0;
            var title = string.IsNullOrWhiteSpace(scene.Title)
                ? $"Scene {scene.OrderIndex + 1}"
                : scene.Title;

            chapterElements.Add(new XElement("chapter",
                new XElement("name", title),
                new XElement("starttime", cumulativeSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
            ));

            cumulativeSeconds += duration;
        }

        var root = new XElement("movie",
            new XElement("title", group.Name),
            new XElement("originaltitle", group.Name),
            new XElement("sorttitle", group.Name),
            group.Date != null ? new XElement("releasedate", group.Date) : null!,
            group.Date != null ? new XElement("year", group.Date.Length >= 4 ? group.Date[..4] : group.Date) : null!,
            new XElement("plot", group.Synopsis ?? string.Empty),
            new XElement("outline", group.Synopsis ?? string.Empty),
            group.Director != null ? new XElement("director", group.Director) : null!,
            group.Studio != null ? new XElement("studio", group.Studio.Name) : null!,
            group.Duration.HasValue ? new XElement("runtime", (group.Duration.Value / 60).ToString(System.Globalization.CultureInfo.InvariantCulture)) : null!,
            // Stash ID for reference
            new XElement("uniqueid", new XAttribute("type", "stash"), new XAttribute("default", tmdbId == null ? "true" : "false"), group.Id),
            // TMDB ID — when present, Jellyfin uses this to match and fetch all metadata + images automatically
            tmdbId != null ? new XElement("uniqueid", new XAttribute("type", "tmdb"), new XAttribute("default", "true"), tmdbId) : null!,
            group.Tags.Count > 0
                ? group.Tags.Select(t => new XElement("genre", t.Name)).ToArray<object>()
                : null!,
            // Embed TMDB poster URL directly so Jellyfin doesn't have to fetch it separately
            tmdbImages?.PosterPath != null
                ? new XElement("thumb",
                    new XAttribute("aspect", "poster"),
                    new XAttribute("preview", $"{TmdbImageBase}{tmdbImages.PosterPath}"),
                    $"{TmdbImageBase}{tmdbImages.PosterPath}")
                : null!,
            // Embed TMDB backdrop/fanart URL
            tmdbImages?.BackdropPath != null
                ? new XElement("thumb",
                    new XAttribute("aspect", "landscape"),
                    new XAttribute("preview", $"{TmdbImageBase}{tmdbImages.BackdropPath}"),
                    $"{TmdbImageBase}{tmdbImages.BackdropPath}")
                : null!,
            tmdbImages?.BackdropPath != null
                ? new XElement("fanart",
                    new XElement("thumb",
                        new XAttribute("preview", $"{TmdbImageBase}{tmdbImages.BackdropPath}"),
                        $"{TmdbImageBase}{tmdbImages.BackdropPath}"))
                : null!
        );

        foreach (var ch in chapterElements)
            root.Add(ch);

        var xml = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        xml.Descendants().Where(e => e.IsEmpty && !e.HasAttributes && string.IsNullOrEmpty(e.Value)).Remove();

        xml.Save(nfoPath);
    }

    /// <summary>
    /// Calls the TMDB API to get poster and backdrop paths for a movie.
    /// </summary>
    private async Task<TmdbImages?> FetchTmdbImagesAsync(string tmdbId, string apiKey, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("StashSync");
            var url = $"{TmdbApiBase}/movie/{tmdbId}?api_key={apiKey}&language=en-US";
            var response = await client.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[StashSync] TMDB API returned {Code} for movie {Id}", response.StatusCode, tmdbId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var posterPath = root.TryGetProperty("poster_path", out var pp) && pp.ValueKind == JsonValueKind.String
                ? pp.GetString()
                : null;

            var backdropPath = root.TryGetProperty("backdrop_path", out var bp) && bp.ValueKind == JsonValueKind.String
                ? bp.GetString()
                : null;

            _logger.LogInformation("[StashSync] TMDB images fetched for movie {Id} — poster: {Poster}", tmdbId, posterPath ?? "none");

            return new TmdbImages(posterPath, backdropPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StashSync] Failed to fetch TMDB images for movie {Id}", tmdbId);
            return null;
        }
    }

    /// <summary>
    /// Parses a TMDB movie ID from a URL like https://www.themoviedb.org/movie/12345
    /// </summary>
    private static string? ExtractTmdbId(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (!uri.Host.EndsWith("themoviedb.org", StringComparison.OrdinalIgnoreCase))
                return null;

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 &&
                segments[0].Equals("movie", StringComparison.OrdinalIgnoreCase))
            {
                var idPart = segments[1].Split('-')[0];
                if (int.TryParse(idPart, out _))
                    return idPart;
            }
        }
        catch (UriFormatException)
        {
            // Not a valid URL
        }

        return null;
    }

    private void WriteChapterMetadata(StashGroup group, PluginConfiguration config, string chaptersPath)
    {
        double cumulativeMs = 0;

        var chaptersEl = new XElement("chapters");
        chaptersEl.Add(new XAttribute("group_id", group.Id));
        chaptersEl.Add(new XAttribute("group_name", group.Name));

        foreach (var scene in group.Scenes)
        {
            var duration = scene.Files.FirstOrDefault()?.Duration ?? 0;
            var streamUrl = ResolveStreamTarget(scene, config);
            var title = string.IsNullOrWhiteSpace(scene.Title)
                ? $"Scene {scene.OrderIndex + 1}"
                : scene.Title;

            var chapterEl = new XElement("chapter",
                new XAttribute("index", scene.OrderIndex),
                new XAttribute("scene_id", scene.Id),
                new XAttribute("title", title),
                new XAttribute("start_ms", (long)cumulativeMs),
                new XAttribute("duration_ms", (long)(duration * 1000)),
                new XAttribute("stream_url", streamUrl)
            );

            chaptersEl.Add(chapterEl);
            cumulativeMs += duration * 1000;
        }

        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), chaptersEl)
            .Save(chaptersPath);
    }

    /// <summary>
    /// Downloads an image to disk. Pass null for config when the URL is already absolute (e.g. TMDB).
    /// </summary>
    private async Task DownloadImageAsync(
        string imageUrl,
        PluginConfiguration? config,
        string destinationPath,
        CancellationToken ct)
    {
        try
        {
            var fullUrl = imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? imageUrl
                : $"{config?.StashUrl.TrimEnd('/')}{imageUrl}";

            using var client = _httpClientFactory.CreateClient("StashSync");

            if (config != null && !string.IsNullOrWhiteSpace(config.StashApiKey))
                client.DefaultRequestHeaders.Add("ApiKey", config.StashApiKey);

            var bytes = await client.GetByteArrayAsync(new Uri(fullUrl), ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(destinationPath, bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StashSync] Failed to download image from {Url}", imageUrl);
        }
    }

    private static string SanitiseFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim().TrimEnd('.');
    }

    /// <summary>Holds poster and backdrop paths returned from the TMDB API.</summary>
    private sealed record TmdbImages(string? PosterPath, string? BackdropPath);
}
