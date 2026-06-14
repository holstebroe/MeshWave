using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Processors;

public class PlaylistProcessor : BaseCatalogueEntryProcessor
{
    public override CatalogueEntryType TargetType => CatalogueEntryType.Playlist;
}
