using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StashSync.Providers;

/// <summary>
/// Metadata provider for StashSync .strm movie entries.
/// Sets the Stash Group external ID so the movie links back to Stash.
/// Chapters are embedded directly in the .nfo file and handled by Jellyfin's built-in NFO reader.
/// </summary>
public class StashGroupMetadataProvider :
    ILocalMetadataProvider<Movie>,
    IHasOrder
{
    private readonly ILogger<StashGroupMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashGroupMetadataProvider"/> class.
    /// </summary>
    public StashGroupMetadataProvider(ILogger<StashGroupMetadataProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "StashSync";

    /// <inheritdoc />
    public int Order => 10; // Run after built-in NFO provider (order 0)

    /// <inheritdoc />
    public Task<IEnumerable<LocalImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<LocalImageInfo>());
    }

    /// <inheritdoc />
    public Task<MetadataResult<Movie>> GetMetadata(
        ItemInfo info,
        IDirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();

        var chapterFile = FindChapterMetadataFile(info.Path);
        if (chapterFile == null)
        {
            // Not a StashSync-managed item — skip silently
            return Task.FromResult(result);
        }

        try
        {
            var groupId = ExtractGroupId(chapterFile);
            if (string.IsNullOrEmpty(groupId))
                return Task.FromResult(result);

            var movie = new Movie();

            // ProviderIds is a plain Dictionary<string, string> on BaseItem
            movie.ProviderIds["Stash"] = groupId;

            result.HasMetadata = true;
            result.Item = movie;

            var itemName = Path.GetFileNameWithoutExtension(info.Path);
            _logger.LogInformation(
                "[StashSync] Linked Stash Group {GroupId} for {Title}",
                groupId,
                itemName);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "[StashSync] IO error reading chapter metadata at {Path}", chapterFile);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException || ex is InvalidOperationException)
        {
            _logger.LogError(ex, "[StashSync] Failed to parse chapter metadata at {Path}", chapterFile);
        }

        return Task.FromResult(result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string? FindChapterMetadataFile(string itemPath)
    {
        var dir = Path.GetDirectoryName(itemPath);
        if (dir == null) return null;

        var candidate = Path.Combine(dir, "chapter-metadata.xml");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ExtractGroupId(string chapterFilePath)
    {
        try
        {
            var doc = XDocument.Load(chapterFilePath);
            return doc.Root?.Attribute("group_id")?.Value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
