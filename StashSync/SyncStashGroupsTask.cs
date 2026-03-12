using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StashSync.GraphQL;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StashSync.Tasks;

/// <summary>
/// Jellyfin scheduled task: "Sync Stash Groups".
/// Triggered manually from Administration → Scheduled Tasks.
/// </summary>
public class SyncStashGroupsTask : IScheduledTask
{
    private readonly StashApiClient _stashClient;
    private readonly StrmWriter _strmWriter;
    private readonly ILogger<SyncStashGroupsTask> _logger;

    public SyncStashGroupsTask(
        StashApiClient stashClient,
        StrmWriter strmWriter,
        ILogger<SyncStashGroupsTask> logger)
    {
        _stashClient = stashClient;
        _strmWriter = strmWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Sync Stash Groups";

    /// <inheritdoc />
    public string Key => "StashSyncGroups";

    /// <inheritdoc />
    public string Description => "Queries Stash App for Groups and creates .strm movie entries in Jellyfin.";

    /// <inheritdoc />
    public string Category => "StashSync";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // No automatic triggers — manual only per user preference
        return Array.Empty<TaskTriggerInfo>();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            _logger.LogError("[StashSync] Plugin configuration not available");
            return;
        }

        _logger.LogInformation("[StashSync] Starting sync from {Url}", config.StashUrl);
        progress.Report(0);

        // Step 1 — fetch all groups
        List<GraphQL.StashGroup> groups;
        try
        {
            groups = await _stashClient.GetAllGroupsAsync(config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StashSync] Failed to fetch groups from Stash");
            throw;
        }

        progress.Report(10);

        if (groups.Count == 0)
        {
            _logger.LogWarning("[StashSync] No groups returned from Stash — nothing to do");
            progress.Report(100);
            return;
        }

        // Step 2 — write .strm + metadata for each group
        int written = 0;
        int skipped = 0;

        for (int i = 0; i < groups.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var group = groups[i];
            try
            {
                bool didWrite = await _strmWriter.WriteGroupAsync(group, config, cancellationToken)
                    .ConfigureAwait(false);

                if (didWrite) written++;
                else skipped++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StashSync] Error writing group '{Name}' ({Id})", group.Name, group.Id);
                skipped++;
            }

            // Progress: 10% → 95% over all groups
            var pct = 10 + (85.0 * (i + 1) / groups.Count);
            progress.Report(pct);
        }

        // Step 3 — cleanup orphaned folders
        _strmWriter.CleanupOrphanedGroups(groups, config);

        progress.Report(100);
        _logger.LogInformation(
            "[StashSync] Sync complete — {Written} written, {Skipped} skipped",
            written, skipped);
    }
}
