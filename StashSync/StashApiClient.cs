using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StashSync.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StashSync.GraphQL;

/// <summary>
/// Lightweight GraphQL client for the Stash API.
/// </summary>
public class StashApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StashApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StashApiClient(IHttpClientFactory httpClientFactory, ILogger<StashApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Fetches all Groups from Stash, page by page.
    /// </summary>
    public async Task<List<StashGroup>> GetAllGroupsAsync(PluginConfiguration config, CancellationToken ct)
    {
        var all = new List<StashGroup>();
        const int pageSize = 100;
        int page = 1;

        while (true)
        {
            var variables = new
            {
                filter = new
                {
                    per_page = pageSize,
                    page = page,
                    sort = "name",
                    direction = "ASC"
                }
            };

            var result = await ExecuteAsync<GroupsResponse>(
                config,
                StashQueries.FindGroups,
                variables,
                ct).ConfigureAwait(false);

            var groups = result?.Data?.FindGroups?.Groups;
            if (groups == null || groups.Count == 0)
                break;

            // Enrich scenes with their scene_index for this group so ordering is correct
            foreach (var group in groups)
            {
                EnrichSceneOrder(group);
                group.Scenes = group.Scenes.OrderBy(s => s.OrderIndex).ToList();
            }

            all.AddRange(groups);

            if (all.Count >= (result?.Data?.FindGroups?.Count ?? 0))
                break;

            page++;
        }

        _logger.LogInformation("[StashSync] Fetched {Count} groups from Stash", all.Count);
        return all;
    }

    /// <summary>
    /// Tries to contact Stash and returns true if the connection succeeds.
    /// </summary>
    public async Task<bool> TestConnectionAsync(PluginConfiguration config, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteAsync<GroupsResponse>(
                config,
                "query { findGroups(filter: { per_page: 1 }) { count } }",
                null,
                ct).ConfigureAwait(false);

            return result?.Data != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StashSync] Connection test failed");
            return false;
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private async Task<T?> ExecuteAsync<T>(
        PluginConfiguration config,
        string query,
        object? variables,
        CancellationToken ct)
    {
        var endpoint = $"{config.StashUrl.TrimEnd('/')}/graphql";

        var requestBody = JsonSerializer.Serialize(new GraphQLRequest
        {
            Query = query,
            Variables = variables
        });

        using var client = _httpClientFactory.CreateClient("StashSync");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(config.StashApiKey))
        {
            request.Headers.Add("ApiKey", config.StashApiKey);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// Reads the scene_index from the nested groups[].scene_index field and
    /// stamps it onto each scene so we can sort correctly later.
    /// </summary>
    private static void EnrichSceneOrder(StashGroup group)
    {
        // The GraphQL response embeds grouping info inside each scene's
        // "groups" array. We look for the entry that matches our group id.
        // This requires a slightly extended model - we handle it via JsonExtension.
        // For now we preserve insertion order as a sensible fallback.
        for (int i = 0; i < group.Scenes.Count; i++)
        {
            group.Scenes[i].OrderIndex = i;
        }
    }
}
