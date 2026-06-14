using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Processors;

public class AlbumProcessor : BaseCatalogueEntryProcessor
{
    public override CatalogueEntryType TargetType => CatalogueEntryType.Album;
}
