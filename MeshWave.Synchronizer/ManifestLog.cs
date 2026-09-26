using System.Text;
using System.Text.Json;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Append-only on-disk format for one manifest stream (<c>*.mwlog</c>), in JSON lines:
/// the first line is a header (the manifest with its snapshot and author key, without operations), every further line
/// is one operation. New operations are appended; the file is only rewritten when the snapshot changes (compaction or
/// adopting a peer's newer snapshot). A torn last line (crash during an append) is ignored when reading.
/// </summary>
public static class ManifestLog
{
    public const string Extension = ".mwlog";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Writes the whole manifest, replacing the file atomically.</summary>
    public static void Rewrite(string path, Manifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";

        lock (manifest)
        {
            var header = new Manifest
            {
                UserId = manifest.UserId,
                StreamType = manifest.StreamType,
                Snapshot = manifest.Snapshot,
                Version = manifest.Version,
                LastUpdated = manifest.LastUpdated,
                AuthorPublicKey = manifest.AuthorPublicKey,
                Operations = []
            };

            using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
            {
                writer.WriteLine(JsonSerializer.Serialize(header, Options));
                foreach (var op in manifest.Operations.OrderBy(o => o.SequenceNumber))
                    writer.WriteLine(JsonSerializer.Serialize(op, Options));
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Appends operations to an existing log.</summary>
    public static void Append(string path, IEnumerable<ManifestOperation> operations)
    {
        var sb = new StringBuilder();
        foreach (var op in operations)
            sb.Append(JsonSerializer.Serialize(op, Options)).Append('\n');
        if (sb.Length > 0)
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Reads a log. Returns null if the file is missing or its header is unreadable.</summary>
    public static Manifest? Read(string path)
    {
        if (!File.Exists(path)) return null;

        Manifest? manifest = null;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (manifest == null)
                {
                    manifest = JsonSerializer.Deserialize<Manifest>(line, Options);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.UserId)) return null;
                    manifest.Operations = [];
                    continue;
                }

                var op = JsonSerializer.Deserialize<ManifestOperation>(line, Options);
                if (op != null) manifest.Operations.Add(op);
            }
            catch (JsonException)
            {
                if (manifest == null) return null;
                break; // torn append
            }
        }

        if (manifest != null)
        {
            // An append may have been repeated after a crash; keep one operation per sequence number.
            manifest.Operations = manifest.Operations
                .GroupBy(o => o.SequenceNumber)
                .Select(g => g.First())
                .OrderBy(o => o.SequenceNumber)
                .ToList();
        }
        return manifest;
    }
}
