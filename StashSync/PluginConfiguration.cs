using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StashSync.Configuration;

/// <summary>
/// Plugin configuration for StashSync.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base URL of the Stash instance (e.g. http://192.168.1.100:9999).
    /// </summary>
    public string StashUrl { get; set; } = "http://localhost:9999";

    /// <summary>
    /// Gets or sets the Stash API key (if authentication is enabled in Stash).
    /// </summary>
    public string StashApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB API key (v3 auth) used to fetch poster URLs.
    /// Free to obtain at https://www.themoviedb.org/settings/api
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local filesystem path where .strm files will be written.
    /// Jellyfin must have this path added as a Movie library.
    /// </summary>
    public string StrmOutputPath { get; set; } = "/media/stash-groups";

    /// <summary>
    /// Gets or sets a value indicating whether to use Stash stream URLs (true)
    /// or local file paths (false) inside the .strm files.
    /// Stream URLs are more network-friendly for cross-machine setups.
    /// </summary>
    public bool UseStreamUrls { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum number of scenes required for a Group to be synced.
    /// Groups with fewer scenes than this will be skipped.
    /// </summary>
    public int MinSceneCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether to include Groups that have no cover image.
    /// </summary>
    public bool IncludeGroupsWithoutCover { get; set; } = true;

    /// <summary>
    /// Gets or sets the base URL of the StashProxy service (e.g. http://192.168.0.38:5678).
    /// When set, .strm files will point to the proxy which streams all scenes concatenated.
    /// Leave empty to use direct Stash stream URLs (single scene only).
    /// </summary>
    public string ProxyUrl { get; set; } = string.Empty;
}
