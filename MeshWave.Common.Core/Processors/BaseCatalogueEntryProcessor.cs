using System;
using System.Collections.Generic;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Processors;

public abstract class BaseCatalogueEntryProcessor : ICatalogueEntryProcessor
{
    public abstract CatalogueEntryType TargetType { get; }

    public virtual bool ValidateUpdate(CatalogueEntry? existingEntry, string? contentHash, Dictionary<string, string> metadata)
    {
        return true;
    }

    public virtual CatalogueEntry MapMetadata(string targetId, string userId, string? contentHash, Dictionary<string, string> metadata, int sequenceNumber, DateTime timestamp)
    {
        return new CatalogueEntry
        {
            EntryId = targetId,
            Type = TargetType,
            OwnerUserId = userId,
            Title = GetTitle(targetId, metadata),
            ArtistName = metadata.GetValueOrDefault("artist") ?? metadata.GetValueOrDefault("artistName"),
            AlbumName = metadata.GetValueOrDefault("album") ?? metadata.GetValueOrDefault("albumName"),
            Duration = ParseDuration(metadata.GetValueOrDefault("duration")),
            ContentHash = contentHash,
            ReleaseDate = ParseDate(metadata.GetValueOrDefault("releasedAt") ?? metadata.GetValueOrDefault("releaseDate")),
            Genre = metadata.GetValueOrDefault("genre"),
            FileSize = long.TryParse(metadata.GetValueOrDefault("fileSize"), out var fs) ? fs : 0,
            Version = int.TryParse(metadata.GetValueOrDefault("version") ?? metadata.GetValueOrDefault("trackVersion"), out var v) ? v : 1,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp
        };
    }

    protected string GetTitle(string targetId, Dictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)) return title;
        if (metadata.TryGetValue("displayName", out var dn) && !string.IsNullOrWhiteSpace(dn)) return dn;
        if (metadata.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        return targetId;
    }

    protected TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (TimeSpan.TryParse(value, out var ts)) return ts;
        if (double.TryParse(value, out var seconds)) return TimeSpan.FromSeconds(seconds);
        return null;
    }

    protected DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return null;
    }
}
