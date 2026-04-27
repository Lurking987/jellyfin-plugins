using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StashMovies;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public override string Name => "Stash Movies Sync";
    public override Guid Id => Guid.Parse("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D");

    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
    }

    // This method registers your settings page in the dashboard
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "StashSyncConfig",
                EmbeddedResourcePath = "Jellyfin.Plugin.StashMovies.Configuration.config.html"
            }
        };
    }
}
