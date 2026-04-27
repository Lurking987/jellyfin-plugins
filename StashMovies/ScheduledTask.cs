using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StashMovies;

public class StashSyncTask : IScheduledTask
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StashSyncTask> _logger;

    public StashSyncTask(IHttpClientFactory httpClientFactory, ILogger<StashSyncTask> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "Sync Stash Movies";
    public string Key => "StashMoviesSync";
    public string Description => "Creates sanitized symlinks and NFOs, overwriting broken links.";
    public string Category => "Library";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var groups = await FetchStashGroups();
        var config = Plugin.Instance!.Configuration;

        if (string.IsNullOrEmpty(config.StashMoviesPath))
        {
            _logger.LogError("Stash Movies Path is not configured.");
            return;
        }

        if (config.SyncMode == SyncMode.Scenes)
        {
            await SyncAsScenes(groups, config, cancellationToken);
        }
        else
        {
            await SyncAsParts(groups, config, cancellationToken);
        }
    }

    // -------------------------------------------------------------------------
    // PARTS MODE
    // Scenes are linked as "Movie Name - pt1.ext", "Movie Name - pt2.ext" etc.
    // Jellyfin merges them into a single movie entry per group.
    // -------------------------------------------------------------------------
    private async Task SyncAsParts(List<StashGroup> groups, PluginConfiguration config, CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            if (config.MinSceneCount > 0 && group.Scenes.Count < config.MinSceneCount)
            {
                _logger.LogInformation("[StashSync] Skipping '{Name}' — only {Count} scene(s), minimum is {Min}.",
                    group.Name, group.Scenes.Count, config.MinSceneCount);
                continue;
            }

            string safeGroupName = Sanitize(group.Name);
            string movieFolderPath = Path.Combine(config.StashMoviesPath, safeGroupName);

            try { Directory.CreateDirectory(movieFolderPath); } catch { }

            // One NFO for the whole group/movie
            string movieNfo = $"<movie><title>{group.Name}</title><plot>{group.Synopsis}</plot></movie>";
            await File.WriteAllTextAsync(Path.Combine(movieFolderPath, "movie.nfo"), movieNfo, cancellationToken);

            // Stash returns scenes in their defined group order already
            var sortedScenes = group.Scenes.ToList();

            for (int i = 0; i < sortedScenes.Count; i++)
            {
                var scene = sortedScenes[i];
                var file = scene.Files.FirstOrDefault();
                if (file == null || string.IsNullOrEmpty(file.Path)) continue;

                string ext = Path.GetExtension(file.Path);
                string partSuffix = $"- pt{i + 1}";
                string movieFileName = $"{safeGroupName} {partSuffix}";
                string symlinkPath = Path.Combine(movieFolderPath, movieFileName + ext);

                ReplaceSymlink(symlinkPath);

                string translatedPath = TranslatePath(file.Path);
                _logger.LogInformation("[StashSync] PART: {Link} -> {Target}", symlinkPath, translatedPath);

                try
                {
                    File.CreateSymbolicLink(symlinkPath, translatedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StashSync] ERROR creating symlink for {Title}", scene.Title);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // SCENES MODE
    // Same single folder + single movie.nfo as Parts mode. The difference is
    // file naming: each symlink uses the scene's own title so Jellyfin exposes
    // them as individually selectable sections rather than auto-playing parts.
    // -------------------------------------------------------------------------
    private async Task SyncAsScenes(List<StashGroup> groups, PluginConfiguration config, CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            if (config.MinSceneCount > 0 && group.Scenes.Count < config.MinSceneCount)
            {
                _logger.LogInformation("[StashSync] Skipping '{Name}' — only {Count} scene(s), minimum is {Min}.",
                    group.Name, group.Scenes.Count, config.MinSceneCount);
                continue;
            }

            string safeGroupName = Sanitize(group.Name);
            string movieFolderPath = Path.Combine(config.StashMoviesPath, safeGroupName);

            try { Directory.CreateDirectory(movieFolderPath); } catch { }

            // One NFO for the whole group/movie
            string movieNfo = $"<movie><title>{group.Name}</title><plot>{group.Synopsis}</plot></movie>";
            await File.WriteAllTextAsync(Path.Combine(movieFolderPath, "movie.nfo"), movieNfo, cancellationToken);

            // Stash returns scenes in their defined group order already
            var sortedScenes = group.Scenes.ToList();

            for (int i = 0; i < sortedScenes.Count; i++)
            {
                var scene = sortedScenes[i];
                var file = scene.Files.FirstOrDefault();
                if (file == null || string.IsNullOrEmpty(file.Path)) continue;

                string ext = Path.GetExtension(file.Path);

                // Use the scene's own title; fall back to a simple index if it has none
                string sceneLabel = !string.IsNullOrWhiteSpace(scene.Title)
                    ? Sanitize(scene.Title)
                    : $"Scene {i + 1}";

                string movieFileName = $"{safeGroupName} - {sceneLabel}";
                string symlinkPath = Path.Combine(movieFolderPath, movieFileName + ext);

                ReplaceSymlink(symlinkPath);

                string translatedPath = TranslatePath(file.Path);
                _logger.LogInformation("[StashSync] SCENE: {Link} -> {Target}", symlinkPath, translatedPath);

                try
                {
                    File.CreateSymbolicLink(symlinkPath, translatedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StashSync] ERROR creating symlink for {Title}", scene.Title);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private void ReplaceSymlink(string path)
    {
        if (File.Exists(path) || PathExists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    private string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Unknown";
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private string TranslatePath(string originalPath)
    {
        var config = Plugin.Instance!.Configuration;

        if (string.IsNullOrEmpty(config.StashPathPrefix) || string.IsNullOrEmpty(config.JellyfinPathPrefix))
            return originalPath;

        if (!string.IsNullOrEmpty(originalPath) &&
            originalPath.StartsWith(config.StashPathPrefix, StringComparison.CurrentCultureIgnoreCase))
        {
            return config.JellyfinPathPrefix + originalPath.Substring(config.StashPathPrefix.Length);
        }

        return originalPath;
    }

    private bool PathExists(string path)
    {
        try
        {
            return File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch { return false; }
    }

    private async Task<List<StashGroup>> FetchStashGroups()
    {
        var config = Plugin.Instance!.Configuration;
        using var client = _httpClientFactory.CreateClient();
        var request = new { query = StashQueries.FindGroups };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(config.StashApiUrl, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StashSync] Failed to contact Stash at {Url}", config.StashApiUrl);
            return new List<StashGroup>();
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("[StashSync] Stash returned HTTP {Code}. Body: {Body}", (int)response.StatusCode, body);
            return new List<StashGroup>();
        }

        var raw = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("[StashSync] Stash response: {Body}", raw);

        GroupsResponse? result;
        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<GroupsResponse>(raw,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StashSync] Failed to deserialize Stash response. Raw: {Body}", raw);
            return new List<StashGroup>();
        }

        var groups = result?.Data?.FindGroups?.Groups ?? new List<StashGroup>();
        _logger.LogInformation("[StashSync] Fetched {Count} group(s) from Stash.", groups.Count);
        return groups;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[] { new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks } };
    }
}

public class GroupsResponse { public GroupsData? Data { get; set; } }
public class GroupsData { public FindGroupsResult? FindGroups { get; set; } }
public class FindGroupsResult { public List<StashGroup> Groups { get; set; } = new(); }
public class StashGroup
{
    public string Name { get; set; } = string.Empty;
    public string? Synopsis { get; set; }
    public List<StashScene> Scenes { get; set; } = new();
}
public class StashScene
{
    public string? Title { get; set; }
    public string? Details { get; set; }
    public List<StashSceneFile> Files { get; set; } = new();
}
public class StashSceneFile { public string Path { get; set; } = string.Empty; }
public static class StashQueries
{
    public const string FindGroups = @"
    query FindGroups {
      findGroups(filter: { per_page: -1 }) {
        groups {
          name
          synopsis
          scenes {
            title
            details
            files { path }
          }
        }
      }
    }";
}
