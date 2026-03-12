using Jellyfin.Plugin.StashSync.GraphQL;
using Jellyfin.Plugin.StashSync.Providers;
using Jellyfin.Plugin.StashSync.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.StashSync;

/// <summary>
/// Registers all StashSync services with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // HTTP client used for all Stash API calls
        serviceCollection.AddHttpClient("StashSync")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = System.TimeSpan.FromSeconds(30);
            });

        // Core services
        serviceCollection.AddSingleton<StashApiClient>();
        serviceCollection.AddSingleton<StrmWriter>();

        // Scheduled task
        serviceCollection.AddSingleton<IScheduledTask, SyncStashGroupsTask>();

        // Metadata providers
        serviceCollection.AddSingleton<ILocalMetadataProvider, StashGroupMetadataProvider>();
        serviceCollection.AddSingleton<IExternalId, StashExternalId>();
    }
}
