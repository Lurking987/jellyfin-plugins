using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StashMovies;

public enum SyncMode
{
    Parts,
    Scenes
}

public class PluginConfiguration : BasePluginConfiguration
{
    public string StashApiUrl { get; set; } = "http://localhost:9999/graphql";
    public string StashMoviesPath { get; set; } = string.Empty;
    public SyncMode SyncMode { get; set; } = SyncMode.Parts;

    /// <summary>
    /// Groups with fewer scenes than this will be skipped entirely. 0 = no minimum.
    /// </summary>
    public int MinSceneCount { get; set; } = 0;

    /// <summary>
    /// The path prefix as Stash sees the files (e.g. /data/Studios).
    /// Leave both fields blank if Stash and Jellyfin share the same mount paths.
    /// </summary>
    public string StashPathPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The equivalent path prefix as Jellyfin sees the same location (e.g. /mnt/stsh).
    /// </summary>
    public string JellyfinPathPrefix { get; set; } = string.Empty;
}
