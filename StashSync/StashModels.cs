using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.StashSync.GraphQL;

// ─── Request ────────────────────────────────────────────────────────────────

/// <summary>Raw GraphQL request envelope.</summary>
public class GraphQLRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("variables")]
    public object? Variables { get; set; }
}

// ─── Groups response ─────────────────────────────────────────────────────────

public class GroupsResponse
{
    [JsonPropertyName("data")]
    public GroupsData? Data { get; set; }
}

public class GroupsData
{
    [JsonPropertyName("findGroups")]
    public FindGroupsResult? FindGroups { get; set; }
}

public class FindGroupsResult
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("groups")]
    public List<StashGroup> Groups { get; set; } = new();
}

// ─── Group ───────────────────────────────────────────────────────────────────

public class StashGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("urls")]
    public List<string> Urls { get; set; } = new();

    [JsonPropertyName("studio")]
    public StashStudio? Studio { get; set; }

    [JsonPropertyName("tags")]
    public List<StashTag> Tags { get; set; } = new();

    [JsonPropertyName("containing_groups")]
    public List<StashGroupGrouping> ContainingGroups { get; set; } = new();

    [JsonPropertyName("sub_groups")]
    public List<StashGroupGrouping> SubGroups { get; set; } = new();

    [JsonPropertyName("front_image_path")]
    public string? FrontImagePath { get; set; }

    [JsonPropertyName("scenes")]
    public List<StashScene> Scenes { get; set; } = new();
}

// ─── Scene ───────────────────────────────────────────────────────────────────

public class StashScene
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("files")]
    public List<StashSceneFile> Files { get; set; } = new();

    [JsonPropertyName("paths")]
    public StashScenePaths? Paths { get; set; }

    /// <summary>
    /// Order index within the group (populated from GroupScene.scene_index).
    /// </summary>
    public int OrderIndex { get; set; }
}

public class StashSceneFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class StashScenePaths
{
    [JsonPropertyName("stream")]
    public string? Stream { get; set; }

    [JsonPropertyName("screenshot")]
    public string? Screenshot { get; set; }
}

// ─── Supporting types ─────────────────────────────────────────────────────────

public class StashStudio
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class StashTag
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class StashGroupGrouping
{
    [JsonPropertyName("group")]
    public StashGroup? Group { get; set; }
}

// ─── GraphQL Queries ─────────────────────────────────────────────────────────

public static class StashQueries
{
    public const string FindGroups = @"
query FindGroups($filter: FindFilterType) {
  findGroups(filter: $filter) {
    count
    groups {
      id
      name
      date
      duration
      synopsis
      director
      urls
      front_image_path
      studio {
        id
        name
      }
      tags {
        id
        name
      }
      scenes {
        id
        title
        date
        details
        files {
          path
          duration
          width
          height
          size
        }
        paths {
          stream
          screenshot
        }
        groups {
          group { id }
          scene_index
        }
      }
    }
  }
}";

    /// <summary>Query to get a single group's scenes with their order index.</summary>
    public const string FindGroupScenes = @"
query FindScene($scene_id: ID!) {
  findScene(id: $scene_id) {
    groups {
      group { id }
      scene_index
    }
  }
}";
}
