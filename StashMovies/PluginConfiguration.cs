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
}
