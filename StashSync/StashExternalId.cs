using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.StashSync.Providers;

/// <summary>
/// Registers "Stash" as an external ID type in Jellyfin.
/// This allows the Group ID to be displayed and linked on the movie's detail page.
/// </summary>
public class StashExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Stash";

    /// <inheritdoc />
    public string Key => "Stash";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string? UrlFormatString
    {
        get
        {
            var baseUrl = Plugin.Instance?.Configuration.StashUrl;
            return string.IsNullOrEmpty(baseUrl)
                ? null
                : $"{baseUrl.TrimEnd('/')}/groups/{{0}}";
        }
    }

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Movie;
}
